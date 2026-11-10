namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// IA, Stage 2: a received spreadsheet travels with the request of its own caption, or with its contiguous text or audio. The
// reference names that message once, so recovering it makes the same request with the same spreadsheet.
public sealed partial class WhatsAppAssistantRequest
{
    /// <summary>The spreadsheet message whose copy and prepared sheet go with this reference's request; null without one.</summary>
    public Guid? AttachmentMessageId { get; private set; }

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
