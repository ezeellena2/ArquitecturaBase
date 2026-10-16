using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppFormActionRepository
{
    Task LockAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetByRunAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<WhatsAppContact?> GetContactForUpdateAsync(Guid recipientUserId, CancellationToken cancellationToken);
    void Add(WhatsAppOutgoingMessage outgoing);
}
