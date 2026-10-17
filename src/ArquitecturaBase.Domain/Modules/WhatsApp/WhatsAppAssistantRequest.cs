using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppAssistantRequestKind { Automatic, Manual }
public enum WhatsAppAssistantRequestState { Pending, Ready, NoReply, Blocked, Discarded, Sent }

/// <summary>A channel reference and a human decision, never another model execution or a copy of its private tools.</summary>
public sealed class WhatsAppAssistantRequest : Entity
{
    private WhatsAppAssistantRequest() { }
    public Guid RequestId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid SubjectUserId { get; private set; }
    public WhatsAppAssistantRequestKind Kind { get; private set; }
    public long ModeRevision { get; private set; }
    public long LinkRevision { get; private set; }
    public string ContextJson { get; private set; } = "";
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

    public bool Ready(Guid proposalId, Guid? definitionId, DateTime nowUtc)
    {
        if (State == WhatsAppAssistantRequestState.Ready) return ProposalId == proposalId && DefinitionId == definitionId;
        if (State != WhatsAppAssistantRequestState.Pending || nowUtc >= ContentExpiresAtUtc || proposalId == Guid.Empty || definitionId == Guid.Empty) return false;
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

    public override string ToString() => "WhatsAppAssistantRequest { Context = [REDACTED] }";
}
