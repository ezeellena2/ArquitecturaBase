using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantReader
{
    Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId, CancellationToken ct);
    Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId, Guid requestId, CancellationToken ct);
    Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid outgoingId, CancellationToken ct);
    Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId, Guid requestId, CancellationToken ct);
    Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId, Guid sourceMessageId, CancellationToken ct);
    Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId, CancellationToken ct);
    /// <summary>The newest business inbound when it is an unanswered tap on an authoring summary's button (IA3-A).</summary>
    Task<WhatsAppAssistantChoiceRow?> FindLatestAuthoringTapAsync(Guid contactId, CancellationToken ct);
    /// <summary>Casos del asistente: the newest business inbound when it is an unanswered tap on a button of a case's question.</summary>
    Task<WhatsAppAssistantChoiceRow?> FindLatestCaseTapAsync(Guid contactId, CancellationToken ct);
    /// <summary>The operation of the latest authoring summary sent to the contact: only its buttons count.</summary>
    Task<Guid?> FindLatestAuthoringRequestAsync(Guid contactId, CancellationToken ct);
    /// <summary>The operation the next message changes, when the latest automatic reference is the reply to «Cambiar algo».</summary>
    Task<Guid?> FindChangeRequestAsync(Guid contactId, CancellationToken ct);
    Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct);
    Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct);
    Task<int> CountOffersAsync(Guid contactId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, Guid? excludingRequestId = null);
    Task<bool> ExistsCurrentContextAsync(WhatsAppAssistantReferenceRow reference, DateTime nowUtc, CancellationToken ct);
    Task<bool> ExistsUnresolvedOfferAsync(Guid contactId, Guid excludingRequestId, CancellationToken ct);
    Task<bool> ExistsDeclinedAsync(Guid contactId, Guid definitionId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListDeclinedDefinitionIdsAsync(Guid subjectUserId, IReadOnlyCollection<Guid> definitionIds, CancellationToken ct);
    Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId, CancellationToken ct);
}
