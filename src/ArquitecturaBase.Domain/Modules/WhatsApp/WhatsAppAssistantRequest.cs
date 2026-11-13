using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppAssistantRequestKind { Automatic, Manual }
public enum WhatsAppAssistantRequestState { Pending, Ready, NoReply, Blocked, Discarded, Sent }

/// <summary>A channel reference and a human decision, never another model execution or a copy of its private tools.</summary>
public sealed partial class WhatsAppAssistantRequest : Entity
{
    private WhatsAppAssistantRequest() { }
    public Guid RequestId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid SubjectUserId { get; private set; }
    public WhatsAppAssistantRequestKind Kind { get; private set; }
    public long ModeRevision { get; private set; }
    public long LinkRevision { get; private set; }
    public string? ContextJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ContentExpiresAtUtc { get; private set; }
    public WhatsAppAssistantRequestState State { get; private set; }
    public Guid? ProposalId { get; private set; }
    public Guid? DefinitionId { get; private set; }
    public Guid? OutgoingId { get; private set; }
    public Guid? QuestionOutgoingId { get; private set; }
    public Guid? DeclinedDefinitionId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? AcceptanceFingerprint { get; private set; }

    public static WhatsAppAssistantRequest Create(Guid requestId, Guid contactId, Guid sourceMessageId, Guid actorUserId,
        Guid subjectUserId, WhatsAppAssistantRequestKind kind, long modeRevision, long linkRevision,
        string contextJson, DateTime createdAtUtc, DateTime contentExpiresAtUtc)
    {
        foreach (var id in new[] { requestId, contactId, sourceMessageId, actorUserId, subjectUserId })
            ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(modeRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(linkRevision);
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(contextJson) || contextJson.Length > 16 * 1024
            || createdAtUtc.Kind != DateTimeKind.Utc || contentExpiresAtUtc.Kind != DateTimeKind.Utc || contentExpiresAtUtc <= createdAtUtc)
            throw new ArgumentException("The assistant reference must have a bounded server context and a UTC lifetime.");
        return new() { RequestId = requestId, ContactId = contactId, SourceMessageId = sourceMessageId, ActorUserId = actorUserId,
            SubjectUserId = subjectUserId, Kind = kind, ModeRevision = modeRevision, LinkRevision = linkRevision, ContextJson = contextJson,
            CreatedAtUtc = createdAtUtc, ContentExpiresAtUtc = contentExpiresAtUtc };
    }

    public bool Ready(Guid? proposalId, Guid? definitionId, DateTime nowUtc)
    {
        if (State == WhatsAppAssistantRequestState.Ready) return ProposalId == proposalId && DefinitionId == definitionId;
        if (State != WhatsAppAssistantRequestState.Pending || nowUtc >= ContentExpiresAtUtc || (IsAlways ? proposalId.HasValue : !proposalId.HasValue) || proposalId == Guid.Empty || definitionId == Guid.Empty) return false;
        ProposalId = proposalId; DefinitionId = definitionId; State = WhatsAppAssistantRequestState.Ready;
        return true;
    }

    public bool FinishWithoutReply(bool blocked, DateTime nowUtc)
    {
        var target = blocked ? WhatsAppAssistantRequestState.Blocked : WhatsAppAssistantRequestState.NoReply;
        if (State == target) return true;
        if (State != WhatsAppAssistantRequestState.Pending && !(blocked && State == WhatsAppAssistantRequestState.Ready)) return false;
        State = target; DecidedAtUtc = nowUtc;
        return true;
    }

    public bool Discard(Guid actorUserId, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty || Kind != WhatsAppAssistantRequestKind.Manual) return false;
        if (State == WhatsAppAssistantRequestState.Discarded) return true;
        if (State != WhatsAppAssistantRequestState.Ready || nowUtc >= ContentExpiresAtUtc) return false;
        State = WhatsAppAssistantRequestState.Discarded; DecidedByUserId = actorUserId; DecidedAtUtc = nowUtc;
        return true;
    }

    public bool Accept(Guid outgoingId, string fingerprint, Guid? actorUserId, DateTime nowUtc)
    {
        if (outgoingId == Guid.Empty || fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigit)
            || (Kind == WhatsAppAssistantRequestKind.Automatic ? actorUserId.HasValue : actorUserId is null || actorUserId == Guid.Empty)) return false;
        if (State == WhatsAppAssistantRequestState.Sent) return OutgoingId == outgoingId && AcceptanceFingerprint == fingerprint;
        if (State != WhatsAppAssistantRequestState.Ready || nowUtc >= ContentExpiresAtUtc) return false;
        OutgoingId = outgoingId; AcceptanceFingerprint = fingerprint; DecidedByUserId = actorUserId; DecidedAtUtc = nowUtc;
        State = WhatsAppAssistantRequestState.Sent;
        return true;
    }

    public bool AttachQuestion(Guid outgoingId,DateTime nowUtc)
    {
        if(Kind!=WhatsAppAssistantRequestKind.Automatic || State!=WhatsAppAssistantRequestState.Ready || DefinitionId.HasValue || outgoingId==Guid.Empty || nowUtc>=ContentExpiresAtUtc) return false;
        if(QuestionOutgoingId.HasValue) return QuestionOutgoingId==outgoingId;
        QuestionOutgoingId=outgoingId; return true;
    }
    public bool SelectDefinition(Guid definitionId,DateTime nowUtc)
    {
        if(State!=WhatsAppAssistantRequestState.Ready || !QuestionOutgoingId.HasValue || definitionId==Guid.Empty || nowUtc>=ContentExpiresAtUtc) return false;
        if(DefinitionId.HasValue) return DefinitionId==definitionId;
        DefinitionId=definitionId; return true;
    }
    public bool RecordDecline(Guid definitionId,DateTime nowUtc)
    {
        if(definitionId==Guid.Empty) return false;
        if(State==WhatsAppAssistantRequestState.NoReply) return DeclinedDefinitionId==definitionId;
        if(State!=WhatsAppAssistantRequestState.Pending || nowUtc>=ContentExpiresAtUtc) return false;
        DeclinedDefinitionId=definitionId; State=WhatsAppAssistantRequestState.NoReply; DecidedAtUtc=nowUtc; return true;
    }

    /// <summary>
    /// Casos del asistente: a reply has up to three actions, in order. The first is this reference; each other one is a reference
    /// of its own (its offer, its outgoing and its checks before sending), with the operation of the first one.
    /// </summary>
    public Guid? OperationRequestId { get; private set; }

    /// <summary>0 for the reference of a message or a capture; 1 or 2 for the second and third actions of the same reply.</summary>
    public int ActionOrdinal { get; private set; }

    /// <summary>What ended without a message («No responder», derivar without the fixed message), with its case, for the chat.</summary>
    public string? OutcomeJson { get; private set; }

    public const int MaxActions = 3;

    public static WhatsAppAssistantRequest CreateAction(WhatsAppAssistantRequest first, Guid requestId, int ordinal, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        if (first.ActionOrdinal != 0 || first.Kind != WhatsAppAssistantRequestKind.Automatic || first.ContextJson is null || first.ActorUserId is null
            || ordinal is < 1 or >= MaxActions || nowUtc >= first.ContentExpiresAtUtc)
            throw new ArgumentException("Another action belongs to the first automatic reference of the same reply, while its content lives.", nameof(first));
        return new()
        {
            RequestId = requestId, ContactId = first.ContactId, SourceMessageId = first.SourceMessageId, ActorUserId = first.ActorUserId,
            SubjectUserId = first.SubjectUserId, Kind = first.Kind, ModeRevision = first.ModeRevision, LinkRevision = first.LinkRevision,
            ContextJson = first.ContextJson, CreatedAtUtc = nowUtc, ContentExpiresAtUtc = first.ContentExpiresAtUtc,
            OperationRequestId = first.OperationRequestId ?? first.RequestId, ActionOrdinal = ordinal,
        };
    }

    /// <summary>
    /// Task 9c of the documents: what answers a reply its capture rejected (the form again after a fixed validation, or the notice of a
    /// repeated one) is a reference of its own on that reply: its outgoing and its checks before sending, with the context, revisions,
    /// lifetime and attachment of the reference that sent the form. It names its own request as the proposal, so it is no offer.
    /// </summary>
    public static WhatsAppAssistantRequest CreateCaptureReply(WhatsAppAssistantRequest sent, Guid requestId, Guid replyMessageId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(replyMessageId, Guid.Empty);
        if (sent.Kind != WhatsAppAssistantRequestKind.Automatic || sent.ContextJson is null || sent.ActorUserId is null || sent.OutgoingId is null
            || replyMessageId == sent.SourceMessageId || nowUtc >= sent.ContentExpiresAtUtc)
            throw new ArgumentException("A capture reply follows the automatic reference that sent its form, while its content lives.", nameof(sent));
        return new()
        {
            RequestId = requestId, ContactId = sent.ContactId, SourceMessageId = replyMessageId, ActorUserId = sent.ActorUserId,
            SubjectUserId = sent.SubjectUserId, Kind = sent.Kind, ModeRevision = sent.ModeRevision, LinkRevision = sent.LinkRevision,
            ContextJson = sent.ContextJson, CreatedAtUtc = nowUtc, ContentExpiresAtUtc = sent.ContentExpiresAtUtc,
            AttachmentMessageId = sent.AttachmentMessageId,
        };
    }

    /// <summary>The outcome of an action that sent nothing, bounded, once; the content retention clears it.</summary>
    public bool RecordOutcome(string outcomeJson)
    {
        if (string.IsNullOrWhiteSpace(outcomeJson) || outcomeJson.Length > 4096 || outcomeJson.TrimStart()[0] != '{') return false;
        if (OutcomeJson is not null) return OutcomeJson == outcomeJson;
        OutcomeJson = outcomeJson;
        return true;
    }

    public void ClearOutcome() => OutcomeJson = null;

    public override string ToString() => "WhatsAppAssistantRequest { Context = [REDACTED] }";
}
