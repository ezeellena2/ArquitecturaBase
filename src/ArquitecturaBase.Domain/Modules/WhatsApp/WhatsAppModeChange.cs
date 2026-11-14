using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// H19: one change of the mode of a conversation, with who made it (nobody when the assistant hands it off) and when. Only added:
/// a later change does not erase who passed the chat to the assistant.
/// </summary>
public sealed class WhatsAppModeChange : Entity
{
    private WhatsAppModeChange()
    {
    }

    public Guid ContactId { get; private set; }

    /// <summary>The revision of the mode this change opened; a contact has one change per revision.</summary>
    public long ModeRevision { get; private set; }

    public WhatsAppConversationMode Mode { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    internal static WhatsAppModeChange Create(Guid contactId, long modeRevision, WhatsAppConversationMode mode, Guid? changedByUserId,
        DateTime changedAtUtc) => new()
    {
        ContactId = contactId,
        ModeRevision = modeRevision,
        Mode = mode,
        ChangedByUserId = changedByUserId,
        ChangedAtUtc = changedAtUtc,
    };
}
