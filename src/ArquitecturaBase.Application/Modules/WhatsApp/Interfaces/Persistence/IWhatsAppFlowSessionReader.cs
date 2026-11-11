using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionReader
{
    Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit, CancellationToken cancellationToken);
    Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId, Guid versionId, string culture, CancellationToken cancellationToken);
    Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId, CancellationToken cancellationToken);
    Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId, string tokenHash, CancellationToken cancellationToken);
    /// <summary>W1-A: the contacts with a send that waits for its location and an answer that has not resolved one yet.</summary>
    Task<IReadOnlyList<Guid>> ListContactsWithPendingLocationsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc,
        CancellationToken cancellationToken);
}
