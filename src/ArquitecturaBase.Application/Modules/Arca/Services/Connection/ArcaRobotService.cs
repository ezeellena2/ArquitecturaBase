using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.Arca.Configuration;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Arranca el lado del cliente del robot (ADR 0026, RB1-A): valida antes de tocar la base, reserva la corrida en el almacén y
/// pasa la clave al worker de este host en memoria recién después de ese commit; nunca a la base.
/// </summary>
internal sealed class ArcaRobotService(IArcaRobotRunReader reader, IArcaRobotRunStore store, IArcaCompanyRobotQueue queue, ICurrentUser currentUser,
    IOptions<ArcaRobotOptions> options, TimeProvider timeProvider, ILogger<ArcaRobotService> logger) : IArcaRobotService
{
    public Task<Result<ArcaRobotRunResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaRobotRunResponse>>(logger, "GetArcaRobotRun", async () => await ResponseAsync(cancellationToken));

    public Task<Result<ArcaRobotRunResponse>> StartAsync(ArcaRobotStartRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaRobotRunResponse>>(logger, "StartArcaRobotRun", async () =>
        {
            if (!options.Value.Enabled) return ArcaConnectionErrors.RobotDisabled;
            if (!request.Consent) return ArcaConnectionErrors.RobotConsentMissing;
            if (CuitNumber.Normalize(request.LoginCuit) is not { } login) return ArcaConnectionErrors.CuitInvalid;
            if (request.FiscalKey.IsEmpty) return ArcaConnectionErrors.RobotKeyMissing;
            var operatorId = currentUser.UserId ?? throw new InvalidOperationException("Starting the ARCA robot needs a signed-in operator.");

            var owner = Guid.NewGuid();
            var reserved = await store.ReserveCompanyAsync(operatorId, owner, request.ExpectedRevision, cancellationToken);
            if (reserved.IsFailure) return reserved.Error;

            if (!queue.TryEnqueue(reserved.Value, new ArcaFiscalCredentials(login, request.FiscalKey)))
            {
                await store.FinishCompanyAsync(reserved.Value.RunId, owner,
                    ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, ArcaRobotSteps.CompanyLogin), cancellationToken);
                return ArcaConnectionErrors.RobotBusy;
            }

            return await ResponseAsync(cancellationToken);
        });

    public Task<Result<ArcaRobotRunResponse>> CompleteByHandAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaRobotRunResponse>>(logger, "CompleteArcaRobotRunByHand", async () =>
        {
            if (!options.Value.Enabled) return ArcaConnectionErrors.RobotDisabled;
            var finished = await store.CompleteCompanyByHandAsync(cancellationToken);
            return finished.IsFailure ? finished.Error : await ResponseAsync(cancellationToken);
        });

    private async Task<ArcaRobotRunResponse> ResponseAsync(CancellationToken cancellationToken) =>
        ArcaRobotRunResponse.From(options.Value.Enabled, await reader.ListAsync(cancellationToken), timeProvider.GetUtcNow().UtcDateTime);
}
