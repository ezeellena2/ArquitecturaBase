using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppFlowReader
{
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<WhatsAppFlowOperationRow?> FindAsync(Guid neutralOperationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListDueDeprecationBindingsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<WhatsAppFlowDeprecationCandidateRow?> FindDeprecationAsync(Guid bindingId, CancellationToken cancellationToken);
    Task<bool> ExistsOpenVersionWorkAsync(Guid versionId, DateTime nowUtc, CancellationToken cancellationToken);
}
