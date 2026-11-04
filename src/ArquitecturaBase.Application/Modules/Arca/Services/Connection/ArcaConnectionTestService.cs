using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// La prueba llama a ARCA con la prueba de cada familia, fuera de toda transacción, y guarda después en una transacción
/// corta, sólo si el certificado probado sigue siendo el que corresponde. Una renovación en rojo no cambia nada: el
/// certificado activo sigue andando.
/// </summary>
internal sealed class ArcaConnectionTestService(IArcaConnectionRepository repository, IArcaConnectionReader reader,
    ArcaServiceCatalogPolicy catalog, IArcaAccessTicketProvider tickets, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<ArcaConnectionTestService> logger) : IArcaConnectionTestService
{
    public Task<Result<ArcaTestResponse>> TestAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaTestResponse>>(logger, "TestArcaConnection", async () =>
        {
            if (await reader.FindAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
            var renewal = await reader.FindSigningCertificateAsync(ArcaCertificateRole.Pending, cancellationToken);
            if ((renewal ?? await reader.FindSigningCertificateAsync(ArcaCertificateRole.Active, cancellationToken)) is not { } tested)
                return ArcaConnectionErrors.CertificateNotLoaded;

            var context = new ArcaServiceContext(tested.ToRef(), tickets);
            List<ArcaServiceCheckResponse> checks = [];
            foreach (var (service, family) in catalog.Chosen(connection.EnabledServices))
                checks.Add(await family.CheckAsync(service, context, cancellationToken));
            var passed = checks.All(check => check.Status == ArcaServiceCheckStatus.Ready);
            if (renewal is not null && !passed) return new ArcaTestResponse(checks, await ResponseAsync(cancellationToken));

            var complete = await catalog.FamiliesCompleteAsync(connection.Id, connection.EnabledServices, cancellationToken);
            var saved = await unitOfWork.ExecuteInTransactionAsync(
                token => RecordCoreAsync(tested, renewal is not null, passed, complete, token), CommitPolicy.OnSuccess, cancellationToken);
            return saved.IsFailure ? saved.Error : new ArcaTestResponse(checks, await ResponseAsync(cancellationToken));
        });

    public Task<Result<ArcaConnectionResponse>> ReevaluateAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaConnectionResponse>>(logger, "ReevaluateArcaConnection", async () =>
        {
            if (await reader.FindAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
            var complete = await catalog.FamiliesCompleteAsync(connection.Id, connection.EnabledServices, cancellationToken);
            var saved = await unitOfWork.ExecuteInTransactionAsync(token => ReevaluateCoreAsync(complete, token), CommitPolicy.OnSuccess, cancellationToken);
            return saved.IsFailure ? saved.Error : await ResponseAsync(cancellationToken);
        });

    private async Task<Result> RecordCoreAsync(ArcaSigningCertificateRow tested, bool renewal, bool passed, bool complete,
        CancellationToken cancellationToken)
    {
        await repository.LockConnectionAsync(cancellationToken);
        if (await repository.GetConnectionAsync(cancellationToken) is not { } connection || connection.Id != tested.ConnectionId)
            return ArcaConnectionErrors.RevisionConflict;
        var role = renewal ? ArcaCertificateRole.Pending : ArcaCertificateRole.Active;
        if (await repository.GetCertificateAsync(connection.Id, role, cancellationToken) is not { IsLoaded: true } certificate
            || certificate.Id != tested.CertificateId)
            return ArcaConnectionErrors.RevisionConflict;

        if (renewal)
        {
            // El viejo se va con sus tickets (cascada) y el nuevo pasa a ser el activo.
            if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Active, cancellationToken) is { } old) repository.Remove(old);
            certificate.Promote();
        }

        connection.RecordTest(passed, complete, timeProvider.GetUtcNow().UtcDateTime);
        return Result.Success();
    }

    private async Task<Result> ReevaluateCoreAsync(bool complete, CancellationToken cancellationToken)
    {
        await repository.LockConnectionAsync(cancellationToken);
        if (await repository.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        connection.ReevaluateFamilies(complete);
        return Result.Success();
    }

    private async Task<ArcaConnectionResponse> ResponseAsync(CancellationToken cancellationToken) =>
        ArcaConnectionResponse.From(await reader.FindAsync(cancellationToken), catalog.Installed);
}
