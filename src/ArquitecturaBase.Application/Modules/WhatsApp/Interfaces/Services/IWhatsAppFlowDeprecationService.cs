using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppFlowDeprecationService
{
    Task<IReadOnlyList<Guid>> ListDueBindingIdsAsync(CancellationToken cancellationToken);
    Task<Result<WhatsAppFlowDeprecationWorkResponse?>> ReserveAsync(Guid? bindingId, CancellationToken cancellationToken);
    Task<Result> MarkEffectStartedAsync(WhatsAppFlowDeprecationWorkResponse work, CancellationToken cancellationToken);
    Task<Result> CompleteEffectAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken cancellationToken);
    Task<Result> CompleteVerificationAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowStateResponse response, CancellationToken cancellationToken);
}
