using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppFlowDispatchService
{
    Task<Result<WhatsAppFlowWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken);
    Task<Result> CompleteEffectAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken cancellationToken);
    Task<Result> CompleteVerificationAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowVerificationResponse response, CancellationToken cancellationToken);
}
