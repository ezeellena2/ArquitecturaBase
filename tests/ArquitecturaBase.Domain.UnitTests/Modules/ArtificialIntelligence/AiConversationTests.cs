using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiConversationTests
{
    [Fact]
    public void Removing_a_conversation_preserves_owner_and_union_of_source_permissions()
    {
        var owner = Guid.NewGuid();
        var now = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        var conversation = AiConversation.Create(owner, now);
        conversation.RequirePermissions(["users.read", "roles.read"]);
        conversation.RequirePermissions(["users.read"]);
        conversation.Remove(now.AddMinutes(1));
        conversation.Remove(now.AddMinutes(2));
        Assert.Equal(owner, conversation.OwnerUserId);
        Assert.Equal(AiConversationState.Removed, conversation.State);
        Assert.Equal(1, conversation.Revision);
        Assert.Equal(new[] { "roles.read", "users.read" }, conversation.RequiredPermissions);
        Assert.Equal(now.AddMinutes(1), conversation.RemovalRequestedAtUtc);
    }
}
