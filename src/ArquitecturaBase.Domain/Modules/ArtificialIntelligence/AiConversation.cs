using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public enum AiConversationState { Active, Removed }

public sealed class AiConversation : Entity, IAuditable
{
    public const int MaximumChannelIdLength = 80;
    private AiConversation() { }
    public Guid OwnerUserId { get; private set; }
    public string ChannelId { get; private set; } = "home";
    public AiConversationState State { get; private set; }
    public long Revision { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public DateTime? RemovalRequestedAtUtc { get; private set; }
    public string[] RequiredPermissions { get; private set; } = [];
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static AiConversation Create(Guid ownerUserId, DateTime nowUtc, string channelId = "home") => new()
    {
        OwnerUserId = ownerUserId, ChannelId = channelId, State = AiConversationState.Active, LastActivityAtUtc = nowUtc,
    };

    public void Touch(DateTime nowUtc)
    {
        LastActivityAtUtc = nowUtc;
        Revision = checked(Revision + 1);
    }

    public void RequirePermissions(IEnumerable<string> permissions)
    {
        RequiredPermissions = RequiredPermissions.Concat(permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public void Remove(DateTime nowUtc)
    {
        if (State == AiConversationState.Removed) return;
        State = AiConversationState.Removed;
        RemovalRequestedAtUtc = nowUtc;
        Touch(nowUtc);
    }
}
