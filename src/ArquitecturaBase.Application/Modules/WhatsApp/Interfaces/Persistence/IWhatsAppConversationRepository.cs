using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppConversationRepository
{
    Task<WhatsAppContact?> GetForUpdateAsync(Guid contactId, CancellationToken cancellationToken);
    Task<WhatsAppReadState?> GetReadStateAsync(Guid contactId, Guid operatorUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contactId, Guid operatorUserId, Guid requestId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid outgoingId, CancellationToken cancellationToken);
    Task<bool> ExistsUnresolvedOutgoingAsync(Guid contactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken cancellationToken);
    Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken cancellationToken);
    /// <summary>
    /// H12 and H31: the head of the next chat that can go out at <paramref name="nowUtc"/>, locked. A contact with a send in
    /// progress, or whose head waits for its next attempt (H7), is not taken; one whose last send finished less than
    /// <see cref="Models.WhatsAppDispatchPacing.RecipientWait"/> ago is still in its wait and goes after the others; between the
    /// chats that can go out, what answers someone goes before a rule's notice. Within a chat the order stays.
    /// </summary>
    Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
    void Add(WhatsAppReadState state);
    void Add(WhatsAppOutgoingMessage outgoing);
}
