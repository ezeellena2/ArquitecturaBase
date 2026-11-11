using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Configuration;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Cada paso guardado del robot en su transacción corta, con el lock de la conexión (ADR 0026): una por método. La carga del
/// certificado es del servicio de la conexión y va con su propia transacción, después de renovar la reserva; la prueba que
/// confirma la delegación la corre el procesador y llega acá como resultado.
/// </summary>
internal sealed class ArcaRobotRunStore(IArcaConnectionRepository connections, IArcaRobotRunRepository runs, ArcaServiceCatalogPolicy catalog,
    IArcaConnectionService connectionService, ArcaRobotAlertPolicy alerts, IOptions<ArcaRobotOptions> options, IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IArcaRobotRunStore
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public async Task<Result<ArcaCompanyRobotWorkResponse>> ReserveCompanyAsync(Guid operatorId, Guid owner, long expectedRevision,
        CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(token => ReserveCompanyCoreAsync(operatorId, owner, expectedRevision, token),
            CommitPolicy.OnSuccess, cancellationToken);

    public Task<bool> BeginStepAsync(Guid runId, Guid owner, string step, CancellationToken cancellationToken) =>
        ChangeAsync(runId, (run, _, nowUtc) => run.Begin(step, owner, nowUtc, options.Value.Lease), cancellationToken);

    public Task<bool> CompleteStepAsync(Guid runId, Guid owner, string step, string? output, CancellationToken cancellationToken) =>
        ChangeAsync(runId, (run, _, nowUtc) => run.Complete(step, output, owner, nowUtc, options.Value.Lease), cancellationToken);

    public Task<bool> FinishCompanyAsync(Guid runId, Guid owner, ArcaRobotOutcome outcome, CancellationToken cancellationToken) =>
        ChangeAsync(runId, (run, other, nowUtc) =>
        {
            if (run.Side != ArcaRobotSide.Company) return false;
            if (outcome is not ArcaRobotOutcome.Failed failed)
            {
                if (!run.Succeed(owner, nowUtc)) return false;
                other?.Wake(nowUtc);
                return true;
            }

            if (!run.Fail(failed.Failure, failed.Step, owner, nowUtc)) return false;
            alerts.Notify(run, failed, nowUtc);
            return true;
        }, cancellationToken);

    public Task<Result> CompleteCompanyByHandAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await connections.LockConnectionAsync(token);
            if (await connections.GetConnectionAsync(token) is not { } connection) return ArcaConnectionErrors.NotConnected;
            if (connection.Mode != ArcaAccessMode.PlatformDelegationAutomated) return ArcaConnectionErrors.RobotModeMismatch;
            if (await runs.GetAsync(connection.Id, ArcaRobotSide.Company, token) is not { } company) return ArcaConnectionErrors.RobotNotStopped;
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var finished = company.CompleteByHand(nowUtc);
            if (finished.IsFailure) return finished;

            if (await runs.GetAsync(connection.Id, ArcaRobotSide.Platform, token) is { } platform) platform.Reschedule(nowUtc);
            else runs.Add(ArcaRobotRun.SchedulePlatform(connection.Id, nowUtc));
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public async Task<ArcaPlatformRobotWorkResponse?> ReservePlatformAsync(Guid runId, Guid owner, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => ReservePlatformCoreAsync(runId, owner, token), CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<bool> InstallPlatformCertificateAsync(Guid runId, Guid owner, string certificatePem, CancellationToken cancellationToken)
    {
        if (!await BeginStepAsync(runId, owner, ArcaRobotSteps.PlatformCertificate, cancellationToken)) return false;
        var current = await connectionService.GetAsync(cancellationToken);
        if (current.IsFailure) return false;
        return (await connectionService.UploadCertificateAsync(certificatePem, current.Value.Revision, cancellationToken)).IsSuccess;
    }

    public Task<bool> FinishPlatformAsync(Guid runId, Guid owner, ArcaRobotOutcome outcome, Result<ArcaTestResponse>? verification,
        CancellationToken cancellationToken) =>
        ChangeAsync(runId, (run, company, nowUtc) =>
        {
            if (run.Side != ArcaRobotSide.Platform) return false;
            if (outcome is ArcaRobotOutcome.Failed failed) return Stopped(run, owner, failed, nowUtc);

            // Sin confirmar porque el cliente no había terminado: si terminó mientras tanto, vuelve enseguida; si no, lo espera.
            if (verification is null)
            {
                var companyDone = company?.Status == ArcaRobotRunStatus.Succeeded;
                return run.Postpone(owner, null, null, companyDone ? nowUtc : nowUtc.Add(options.Value.CompanyWait));
            }

            return Verified(run, owner, verification, nowUtc);
        }, cancellationToken);

    private async Task<Result<ArcaCompanyRobotWorkResponse>> ReserveCompanyCoreAsync(Guid operatorId, Guid owner, long expectedRevision,
        CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await connections.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        if (connection.Mode != ArcaAccessMode.PlatformDelegationAutomated || connection.PlatformCuit is not { } platformCuit)
            return ArcaConnectionErrors.RobotModeMismatch;
        if (connection.Revision != expectedRevision) return ArcaConnectionErrors.RevisionConflict;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var lease = options.Value.Lease;
        var company = await runs.GetAsync(connection.Id, ArcaRobotSide.Company, cancellationToken);
        if (company is null)
        {
            company = ArcaRobotRun.StartCompany(connection.Id, operatorId, owner, nowUtc, lease);
            runs.Add(company);
        }
        else if (company.RestartCompany(operatorId, owner, nowUtc, lease) is { IsFailure: true } busy)
        {
            return busy.Error;
        }

        // El lado de la plataforma empieza junto con el del cliente: crea el certificado y autoriza mientras el cliente delega.
        if (await runs.GetAsync(connection.Id, ArcaRobotSide.Platform, cancellationToken) is { } platform) platform.Reschedule(nowUtc);
        else runs.Add(ArcaRobotRun.SchedulePlatform(connection.Id, nowUtc));

        var delegated = catalog.DelegatedCodes;
        return new ArcaCompanyRobotWorkResponse(company.Id, owner, connection.Cuit, platformCuit,
            [.. connection.EnabledServices.Where(delegated.Contains)], [.. connection.EnabledServices]);
    }

    private async Task<Result<ArcaPlatformRobotWorkResponse?>> ReservePlatformCoreAsync(Guid runId, Guid owner, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (await runs.GetAsync(runId, cancellationToken) is not { Side: ArcaRobotSide.Platform } run) return Result.Success<ArcaPlatformRobotWorkResponse?>(null);
        if (await connections.GetConnectionAsync(cancellationToken) is not { PlatformCuit: { } platformCuit } connection || connection.Id != run.ConnectionId)
            return Result.Success<ArcaPlatformRobotWorkResponse?>(null);
        if (!run.TryReserve(owner, nowUtc, options.Value.Lease)) return Result.Success<ArcaPlatformRobotWorkResponse?>(null);

        var active = await connections.GetCertificateAsync(connection.Id, ArcaCertificateRole.Active, cancellationToken);
        var pending = await connections.GetCertificateAsync(connection.Id, ArcaCertificateRole.Pending, cancellationToken);
        if ((active ?? pending) is not { } certificate)
        {
            run.Fail(ArcaRobotFailure.ScreenChanged, ArcaRobotSteps.PlatformCertificate, owner, nowUtc);
            return Result.Success<ArcaPlatformRobotWorkResponse?>(null);
        }

        var company = await runs.GetAsync(connection.Id, ArcaRobotSide.Company, cancellationToken);
        var companyDone = company?.Status == ArcaRobotRunStatus.Succeeded;
        var delegated = catalog.DelegatedCodes;
        var requestPem = active is { IsLoaded: true } ? null : (pending ?? active)?.RequestPem;
        string[] authorized = [.. connection.EnabledServices.Where(code => !delegated.Contains(code))];

        // Sin nada que hacer antes de que el cliente delegue, no entra a ARCA: espera a que el cliente lo despierte.
        var pendingBeforeCompany = requestPem is not null
            || authorized.Any(code => !run.CompletedSteps.Contains(ArcaRobotSteps.PlatformAuthorize(ArcaServiceCode.Create(code)!.Value), StringComparer.Ordinal));
        if (!companyDone && !pendingBeforeCompany)
        {
            run.Postpone(owner, null, null, nowUtc.Add(options.Value.CompanyWait));
            return Result.Success<ArcaPlatformRobotWorkResponse?>(null);
        }

        return Result.Success<ArcaPlatformRobotWorkResponse?>(new ArcaPlatformRobotWorkResponse(run.Id, owner, connection.Cuit, platformCuit,
            certificate.Alias, requestPem, [.. run.CompletedSteps], companyDone, [.. connection.EnabledServices.Where(delegated.Contains)], authorized));
    }

    /// <summary>ARCA sin responder o un host caído esperan con su espera creciente; lo demás frena y avisa si corresponde.</summary>
    private bool Stopped(ArcaRobotRun run, Guid owner, ArcaRobotOutcome.Failed failed, DateTime nowUtc)
    {
        if (failed.Failure is ArcaRobotFailure.Unavailable or ArcaRobotFailure.Interrupted && run.Attempts < options.Value.MaxAttempts)
            return run.Postpone(owner, failed.Failure, failed.Step, nowUtc.Add(RetryDelay(run.Attempts)));
        if (!run.Fail(failed.Failure, failed.Step, owner, nowUtc)) return false;
        alerts.Notify(run, failed, nowUtc);
        return true;
    }

    /// <summary>
    /// La prueba en verde para todos los servicios termina el trabajo. ARCA bloqueando otro ticket o tardando en reflejar la
    /// delegación espera; si después de los intentos sigue sin reflejarla, el robot leyó mal una pantalla y avisa.
    /// </summary>
    private bool Verified(ArcaRobotRun run, Guid owner, Result<ArcaTestResponse> tested, DateTime nowUtc)
    {
        if (tested.IsFailure) return run.Postpone(owner, null, ArcaRobotSteps.Verify, nowUtc.Add(RetryDelay(run.Attempts)));
        var checks = tested.Value.Checks;
        if (checks.Count > 0 && checks.All(check => check.Status == ArcaServiceCheckStatus.Ready))
            return run.Complete(ArcaRobotSteps.Verify, null, owner, nowUtc, options.Value.Lease) && run.Succeed(owner, nowUtc);

        if (checks.Where(check => check.Status == ArcaServiceCheckStatus.Blocked).Max(check => check.RetryAfterUtc) is { } blockedUntil)
            return run.Postpone(owner, null, ArcaRobotSteps.Verify, blockedUntil > nowUtc ? blockedUntil : nowUtc.Add(options.Value.RetryDelay));

        var notReflected = checks.Any(check => check.Status is ArcaServiceCheckStatus.NotDelegated or ArcaServiceCheckStatus.NotAuthorized);
        if (run.Attempts < options.Value.MaxAttempts)
            return run.Postpone(owner, notReflected ? null : ArcaRobotFailure.Unavailable, ArcaRobotSteps.Verify, nowUtc.Add(RetryDelay(run.Attempts)));

        var failure = notReflected ? ArcaRobotFailure.ScreenChanged : ArcaRobotFailure.Unavailable;
        if (!run.Fail(failure, ArcaRobotSteps.Verify, owner, nowUtc)) return false;
        alerts.Notify(run, (ArcaRobotOutcome.Failed)ArcaRobotOutcome.Stopped(failure, ArcaRobotSteps.Verify), nowUtc);
        return true;
    }

    private TimeSpan RetryDelay(int attempts)
    {
        var delay = options.Value.RetryDelay * Math.Pow(2, Math.Max(0, attempts - 1));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    /// <summary>Un cambio sobre un lado, con el otro lado de la misma conexión a mano, en una transacción.</summary>
    private async Task<bool> ChangeAsync(Guid runId, Func<ArcaRobotRun, ArcaRobotRun?, DateTime, bool> change, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await connections.LockConnectionAsync(token);
            if (await runs.GetAsync(runId, token) is not { } run) return Result.Success(false);
            var otherSide = run.Side == ArcaRobotSide.Company ? ArcaRobotSide.Platform : ArcaRobotSide.Company;
            var other = await runs.GetAsync(run.ConnectionId, otherSide, token);
            return Result.Success(change(run, other, timeProvider.GetUtcNow().UtcDateTime));
        }, CommitPolicy.OnSuccess, cancellationToken)).Value;
}
