using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormMessageDispatchService(IWhatsAppFlowSessionReader sessions,
    WhatsAppFlowSessionVerifier verifier, WhatsAppFormDispatchPolicy policy,
    ILogger<WhatsAppFormMessageDispatchService> logger) : IWhatsAppFormMessageDispatchService
{
    public Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(Guid outgoingId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppFlowMessageResponse>>(logger, "PrepareWhatsAppFlowMessage", async () =>
        {
            var row = await sessions.FindDispatchAsync(outgoingId, cancellationToken);
            if (row is null) return FlowErrors.StateConflict;
            var verified = verifier.Verify(row);
            if (verified.IsFailure) return verified.Error;
            var content = verifier.ReadAssistant(row);
            return content.IsFailure ? content.Error : await policy.PrepareAsync(row, verified.Value, content.Value, cancellationToken);
        });
}
