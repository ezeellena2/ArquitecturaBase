using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppFormActionRepository
{
    Task LockAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetByRunAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetByRunForUpdateAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<WhatsAppContact?> GetContactForUpdateAsync(Guid recipientUserId, CancellationToken cancellationToken);
    /// <summary>The contact's inbound taps with these ids, tracked to mark them handled, in one read.</summary>
    Task<IReadOnlyList<WhatsAppMessage>> ListTapsAsync(Guid contactId, IReadOnlyCollection<Guid> tapIds, CancellationToken cancellationToken);
    void Add(WhatsAppOutgoingMessage outgoing);
}
