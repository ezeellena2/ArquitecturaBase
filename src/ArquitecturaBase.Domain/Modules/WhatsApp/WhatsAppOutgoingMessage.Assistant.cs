using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppOutgoingMessage
{
    public long? AssistantModeRevision { get; private set; }

    /// <summary>
    /// Derivar (AS4): the fixed message of a handoff. It goes out after the conversation passed to Atención manual, so its
    /// mode revision is the one right after the handoff, and only while the conversation still needs a person.
    /// </summary>
    public bool IsAssistantHandoff { get; private set; }

    /// <summary>
    /// Casos del asistente: why the assistant sent this message (the case, what it used and its place, «1 de 3»), as its owner
    /// wrote it. «Por qué respondió esto» reads it; it is never sent and it goes away with the text.
    /// </summary>
    public string? AssistantReplyJson { get; private set; }

    public const int MaxAssistantReplyJsonLength = 8192;
    public string? QuickRepliesJson { get; private set; }
    public IReadOnlyList<WhatsAppOutgoingQuickReply> QuickReplies => QuickRepliesJson is null ? [] :
        JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(QuickRepliesJson)!;

    public static WhatsAppOutgoingMessage CreateAssistantText(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision) =>
        CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, WhatsAppMessage.MaxBodyLength);

    /// <summary>Derivar (AS4): the fixed message, with the mode revision of the conversation after the handoff.</summary>
    public static WhatsAppOutgoingMessage CreateAssistantHandoff(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long manualModeRevision)
    {
        var outgoing = CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, manualModeRevision, WhatsAppMessage.MaxBodyLength);
        outgoing.IsAssistantHandoff = true;
        return outgoing;
    }

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
                item.Title.Length > 20 || !IsVisibleBody(item.Title) || item.DefinitionRevision < 0))
            throw new ArgumentException("Quick replies must have unique bounded IDs and visible titles.", nameof(quickReplies));
        var outgoing = CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, 1024);
        outgoing.Kind = WhatsAppOutgoingMessageKind.QuickReplies; outgoing.QuickRepliesJson = JsonSerializer.Serialize(quickReplies);
        return outgoing;
    }

    /// <summary>W1-A, sent by the assistant: the location request that goes before the Flow of its send.</summary>
    public static WhatsAppOutgoingMessage CreateAssistantLocationRequest(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, Guid sessionId, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        var outgoing = CreateAssistant(recipientUserId, contactId, requestId, body, nowUtc, contactLinkRevision, recipientWaId, modeRevision, 1024);
        outgoing.Kind = WhatsAppOutgoingMessageKind.LocationRequest; outgoing.FlowSessionId = sessionId;
        return outgoing;
    }

    /// <summary>W1-A, sent by the assistant: «Seguir sin ubicación» for an optional location; its id names the send.</summary>
    public static WhatsAppOutgoingMessage CreateAssistantLocationSkip(Guid recipientUserId, Guid contactId, Guid requestId,
        string body, string title, Guid sessionId, DateTime nowUtc, long contactLinkRevision, string recipientWaId, long modeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        var outgoing = CreateAssistantQuickReplies(recipientUserId, contactId, requestId, body,
            [new WhatsAppOutgoingQuickReply(WhatsAppFlowLocationReply.Id(sessionId), title, 0)], nowUtc, contactLinkRevision, recipientWaId, modeRevision);
        outgoing.FlowSessionId = sessionId;
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

    /// <summary>Casos del asistente: the why of a reply, set once when the message is queued.</summary>
    public void ExplainAssistantReply(string replyJson)
    {
        if (Origin != WhatsAppMessageOrigin.Assistant || State != WhatsAppOutgoingMessageState.Pending)
            throw new InvalidOperationException("Only a pending assistant message carries the why of a reply.");
        if (string.IsNullOrWhiteSpace(replyJson) || replyJson.Length > MaxAssistantReplyJsonLength || replyJson.TrimStart()[0] != '{')
            throw new ArgumentException("The why of a reply is a bounded JSON object.", nameof(replyJson));
        if (AssistantReplyJson is not null && AssistantReplyJson != replyJson)
            throw new InvalidOperationException("The why of a reply is set once.");
        AssistantReplyJson = replyJson;
    }

    public Guid ReserveAssistant(DateTime nowUtc) => ReserveSystemDispatch(WhatsAppMessageOrigin.Assistant, nowUtc);
    public bool OwnsAssistantReservation(Guid reservationId) => OwnsSystemReservation(WhatsAppMessageOrigin.Assistant, reservationId);
    public bool MarkAssistantMessageEffect(Guid reservationId, DateTime nowUtc) => MarkSystemMessageEffect(WhatsAppMessageOrigin.Assistant, reservationId, nowUtc);
    public void RecoverExpiredAssistant() => RecoverExpiredSystemDispatch(WhatsAppMessageOrigin.Assistant);
}
