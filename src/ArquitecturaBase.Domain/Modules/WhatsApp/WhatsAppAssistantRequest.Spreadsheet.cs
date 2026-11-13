namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// IA, Stage 2: a received spreadsheet travels with the request of its own caption, or with its contiguous text or audio. The
// reference names that message once, so recovering it makes the same request with the same spreadsheet.
public sealed partial class WhatsAppAssistantRequest
{
    /// <summary>The spreadsheet message whose copy and prepared sheet go with this reference's request; null without one.</summary>
    public Guid? AttachmentMessageId { get; private set; }

    /// <summary>The most loads a spreadsheet sends to its person (VC1-A, task 29).</summary>
    public const int MaxLoads = 3;

    /// <summary>
    /// VC1-A (task 29): each load of a spreadsheet is a reference of its own (its Flow and its checks before sending), like another action of
    /// a reply, and carries the spreadsheet of the first one, so it is no offer and the chat marks its Flow as sent for a file. The first
    /// reference keeps the notice; the loads take the ordinals 1 to <see cref="MaxLoads"/>.
    /// </summary>
    public static WhatsAppAssistantRequest CreateLoad(WhatsAppAssistantRequest first, Guid requestId, int ordinal, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        if (first.ActionOrdinal != 0 || first.Kind != WhatsAppAssistantRequestKind.Automatic || first.ContextJson is null || first.ActorUserId is null
            || first.AttachmentMessageId is null || ordinal is < 1 or > MaxLoads || nowUtc >= first.ContentExpiresAtUtc)
            throw new ArgumentException("A load belongs to the first automatic reference that carries the spreadsheet, while its content lives.", nameof(first));
        return new()
        {
            RequestId = requestId, ContactId = first.ContactId, SourceMessageId = first.SourceMessageId, ActorUserId = first.ActorUserId,
            SubjectUserId = first.SubjectUserId, Kind = first.Kind, ModeRevision = first.ModeRevision, LinkRevision = first.LinkRevision,
            ContextJson = first.ContextJson, CreatedAtUtc = nowUtc, ContentExpiresAtUtc = first.ContentExpiresAtUtc,
            OperationRequestId = first.OperationRequestId ?? first.RequestId, ActionOrdinal = ordinal, AttachmentMessageId = first.AttachmentMessageId,
        };
    }

    /// <summary>Only while pending, and never after a captured response: what follows a form carries no spreadsheet.</summary>
    public bool CarryAttachment(Guid messageId)
    {
        if (messageId == Guid.Empty || SourceRecordId.HasValue) return false;
        if (AttachmentMessageId.HasValue) return AttachmentMessageId == messageId;
        if (State != WhatsAppAssistantRequestState.Pending) return false;
        AttachmentMessageId = messageId;
        return true;
    }
}
