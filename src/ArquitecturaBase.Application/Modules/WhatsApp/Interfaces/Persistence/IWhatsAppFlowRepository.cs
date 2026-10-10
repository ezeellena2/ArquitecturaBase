using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppFlowRepository
{
    Task LockAsync(Guid neutralOperationId, CancellationToken cancellationToken);
    Task<WhatsAppFlowOperation?> GetAsync(Guid neutralOperationId, CancellationToken cancellationToken);
    Task<WhatsAppFlowOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<WhatsAppFlowBinding>> ListBindingsAsync(Guid operationId, CancellationToken cancellationToken);
    void Add(WhatsAppFlowOperation operation);
    void AddBinding(WhatsAppFlowBinding binding);
}
