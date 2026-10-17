using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppOutgoingMessage
{
    public long? AssistantModeRevision { get; private set; }
    public string? QuickRepliesJson { get; private set; }
    public IReadOnlyList<WhatsAppOutgoingQuickReply> QuickReplies => QuickRepliesJson is null ? [] :
        JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(QuickRepliesJson)!;

    public static WhatsAppOutgoingMessage CreateAssistantText(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision) =>
        CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, WhatsAppMessage.MaxBodyLength);

    public static WhatsAppOutgoingMessage CreateAssistantFlow(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, Guid sessionId, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        var outgoing = CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, 1024);
        outgoing.Kind = WhatsAppOutgoingMessageKind.Flow; outgoing.FlowSessionId = sessionId;
        return outgoing;
    }

    public static WhatsAppOutgoingMessage CreateAssistantQuickReplies(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, IReadOnlyList<WhatsAppOutgoingQuickReply> quickReplies, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision)
    {
        if (quickReplies.Count is < 1 or > 3 || quickReplies.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != quickReplies.Count ||
            quickReplies.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 256 || item.Id.Any(char.IsControl) || item.Id != item.Id.Trim() ||
                item.Title.Length > 20 || !IsVisibleBody(item.Title)))
            throw new ArgumentException("Quick replies must have unique bounded IDs and visible titles.", nameof(quickReplies));
        var outgoing = CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, 1024);
        outgoing.Kind = WhatsAppOutgoingMessageKind.QuickReplies; outgoing.QuickRepliesJson = JsonSerializer.Serialize(quickReplies);
        return outgoing;
    }

    private static WhatsAppOutgoingMessage CreateAssistant(Guid recipientUserId, Guid contactId, Guid requestId, string body,
        DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision, int maximumBodyLength)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(recipientUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(contactLinkRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(modeRevision);
        if (!WhatsAppContact.IsValidWaId(recipientWaId)) throw new ArgumentException("The captured recipient is invalid.", nameof(recipientWaId));
        if (!IsVisibleBody(body) || body.Length > maximumBodyLength) throw new ArgumentException("The assistant body must be visible and fit the provider limit.", nameof(body));
        return new WhatsAppOutgoingMessage
        {
            Origin = WhatsAppMessageOrigin.Assistant, RecipientUserId = recipientUserId, ContactId = contactId, RequestId = requestId,
            Body = body, RequestedAtUtc = nowUtc, RecipientLinkRevision = contactLinkRevision, RecipientWaId = recipientWaId, AssistantModeRevision = modeRevision,
        };
    }

    public Guid ReserveAssistant(DateTime nowUtc) => ReserveSystemDispatch(WhatsAppMessageOrigin.Assistant, nowUtc);
    public bool OwnsAssistantReservation(Guid reservationId) => OwnsSystemReservation(WhatsAppMessageOrigin.Assistant, reservationId);
    public bool MarkAssistantMessageEffect(Guid reservationId, DateTime nowUtc) => MarkSystemMessageEffect(WhatsAppMessageOrigin.Assistant, reservationId, nowUtc);
    public void RecoverExpiredAssistant() => RecoverExpiredSystemDispatch(WhatsAppMessageOrigin.Assistant);
}
