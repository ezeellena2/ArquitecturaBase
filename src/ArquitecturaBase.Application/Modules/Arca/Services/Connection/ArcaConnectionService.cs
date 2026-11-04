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

internal sealed class ArcaConnectionService(IArcaConnectionRepository repository, IArcaConnectionReader reader,
    IArcaCredentialService credentials, ArcaServiceCatalog catalog, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<ArcaConnectionService> logger) : IArcaConnectionService
{
    public Task<Result<ArcaConnectionResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaConnectionResponse>>(logger, "GetArcaConnection", async () => await ResponseAsync(cancellationToken));

    public Task<Result<ArcaConnectionResponse>> StartCertificateRequestAsync(string cuit, ArcaEnvironment environment, string alias,
        IReadOnlyCollection<string> services, long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "StartArcaCertificateRequest", async () =>
        {
            if (!ArcaCertificate.IsValidAlias(alias)) return ArcaConnectionErrors.AliasInvalid;
            if (Codes(services) is not { } chosen) return ArcaConnectionErrors.ServiceNotInstalled;
            return await SaveAsync(token => StartCoreAsync(cuit, environment, alias, chosen, expectedRevision, token), cancellationToken);
        });

    public Task<Result<ArcaConnectionResponse>> ChooseServicesAsync(IReadOnlyCollection<string> services, long expectedRevision,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "ChooseArcaServices", async () =>
        {
            if (Codes(services) is not { } chosen) return ArcaConnectionErrors.ServiceNotInstalled;
            return await SaveAsync(token => ChangeCoreAsync(expectedRevision,
                (connection, _) => Task.FromResult(connection.ChooseServices(chosen, catalog.InstalledCodes, catalog.RequiredCodes)), token), cancellationToken);
        });

    public Task<Result<ArcaPendingRequestResponse>> GetPendingRequestAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaPendingRequestResponse>>(logger, "GetArcaPendingRequest", async () =>
            await reader.FindPendingRequestAsync(cancellationToken) is { } pending
                ? new ArcaPendingRequestResponse(pending.Alias, pending.RequestPem)
                : ArcaConnectionErrors.NoPendingRequest);

    public Task<Result<ArcaConnectionResponse>> CancelRenewalAsync(long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CancelArcaRenewal", () => SaveAsync(token => ChangeCoreAsync(expectedRevision, async (connection, ct) =>
        {
            if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Pending, ct) is not { } pending)
                return ArcaConnectionErrors.NoPendingRequest;
            repository.Remove(pending);
            connection.RequestCancelled();
            return Result.Success();
        }, token), cancellationToken));

    public Task<Result<ArcaConnectionResponse>> UploadCertificateAsync(string certificatePem, long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "UploadArcaCertificate", () => SaveAsync(token => ChangeCoreAsync(expectedRevision, async (connection, ct) =>
        {
            if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Pending, ct) is not { } pending)
                return ArcaConnectionErrors.NoPendingRequest;
            var inspected = credentials.Inspect(connection.Id, pending.Id, pending.ProtectedPrivateKey, certificatePem, connection.Cuit,
                connection.Environment, timeProvider.GetUtcNow().UtcDateTime);
            if (inspected.IsFailure) return inspected.Error;

            pending.Load(inspected.Value.CertificatePem, inspected.Value.NotBeforeUtc, inspected.Value.NotAfterUtc, inspected.Value.Thumbprint);
            // En el alta queda activo; en una renovación espera la prueba, que lo promueve si sale en verde.
            if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Active, ct) is null) pending.Promote();
            connection.CertificateLoaded();
            return Result.Success();
        }, token), cancellationToken));

    public Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "DisconnectArca", () => unitOfWork.ExecuteInTransactionAsync(token => ChangeCoreAsync(expectedRevision, (connection, _) =>
        {
            repository.Remove(connection);
            return Task.FromResult(Result.Success());
        }, token), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> StartCoreAsync(string cuit, ArcaEnvironment environment, string alias, IReadOnlyCollection<ArcaServiceCode> chosen,
        long expectedRevision, CancellationToken cancellationToken)
    {
        await repository.LockConnectionAsync(cancellationToken);
        var connection = await repository.GetConnectionAsync(cancellationToken);
        if ((connection?.Revision ?? 0) != expectedRevision) return ArcaConnectionErrors.RevisionConflict;

        if (connection is null)
        {
            var started = ArcaConnection.Start(cuit, environment, chosen, catalog.InstalledCodes, catalog.RequiredCodes);
            if (started.IsFailure) return started.Error;
            connection = started.Value;
            repository.Add(connection);
        }
        else
        {
            // Una conexión que ya existe pide otro certificado para sí misma: la CUIT, el ambiente y los servicios son los suyos.
            if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Pending, cancellationToken) is { } previous)
                repository.Remove(previous);
            connection.CertificateRequested();
        }

        var certificateId = Guid.CreateVersion7();
        var request = credentials.CreateRequest(connection.Id, certificateId, connection.Cuit, alias);
        var certificate = ArcaCertificate.Request(certificateId, connection.Id, alias, request.RequestPem, request.ProtectedPrivateKey);
        if (certificate.IsFailure) return certificate.Error;
        repository.Add(certificate.Value);
        return Result.Success();
    }

    private async Task<Result> ChangeCoreAsync(long expectedRevision, Func<ArcaConnection, CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        await repository.LockConnectionAsync(cancellationToken);
        if (await repository.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        if (connection.Revision != expectedRevision) return ArcaConnectionErrors.RevisionConflict;
        return await change(connection, cancellationToken);
    }

    private async Task<Result<ArcaConnectionResponse>> SaveAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.ExecuteInTransactionAsync(work, CommitPolicy.OnSuccess, cancellationToken);
        return saved.IsFailure ? saved.Error : await ResponseAsync(cancellationToken);
    }

    private async Task<ArcaConnectionResponse> ResponseAsync(CancellationToken cancellationToken) =>
        ArcaConnectionResponse.From(await reader.FindAsync(cancellationToken), catalog.Installed);

    /// <summary>Los códigos pedidos; null si alguno no es un código de servicio válido.</summary>
    private static ArcaServiceCode[]? Codes(IReadOnlyCollection<string> services)
    {
        var codes = services.Select(ArcaServiceCode.Create).ToArray();
        return codes.All(code => code is not null) ? [.. codes.Select(code => code!.Value)] : null;
    }
}
