using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionReader
{
    Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit, CancellationToken cancellationToken);
    Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId, Guid versionId, string culture, CancellationToken cancellationToken);
    Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId, CancellationToken cancellationToken);
    Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId, string tokenHash, CancellationToken cancellationToken);
}
