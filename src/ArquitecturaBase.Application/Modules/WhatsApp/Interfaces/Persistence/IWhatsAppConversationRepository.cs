using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppConversationRepository
{
    Task<WhatsAppContact?> GetForUpdateAsync(Guid contactId, CancellationToken cancellationToken);
    Task<WhatsAppReadState?> GetReadStateAsync(Guid contactId, Guid operatorUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contactId, Guid operatorUserId, Guid requestId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid outgoingId, CancellationToken cancellationToken);
    Task<bool> ExistsUnresolvedOutgoingAsync(Guid contactId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
    void Add(WhatsAppReadState state);
    void Add(WhatsAppOutgoingMessage outgoing);
}
