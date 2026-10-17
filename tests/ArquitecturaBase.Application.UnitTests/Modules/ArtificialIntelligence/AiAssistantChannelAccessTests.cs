using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantChannelAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_captured_manual_context_revalidates_both_identities_without_requiring_home_permission()
    {
        var users = new InMemoryUserAccounts(); var actor = users.AddUser("operator@example.test"); var subject = users.AddUser("recipient@example.test");
        var permissions = new FakePermissionService(); permissions.Permissions[actor.Id] = ["fictional.channel.read", "fictional.channel.send"];
        permissions.Permissions[subject.Id] = ["fictional.records.create"];
        var context = Context(actor.Id, subject.Id); var guard = Guard(users, permissions, subject.Id);
        Assert.True((await guard.AuthorizeAsync(context, Ct)).IsSuccess);
        permissions.Permissions[subject.Id] = [];
        Assert.Equal(AiAssistantErrors.Forbidden, (await guard.AuthorizeAsync(context, Ct)).Error);
        Assert.Equal(new[]{"fictional.records.create"}, context.RequiredPermissions.Subject);
        permissions.Permissions[subject.Id] = ["fictional.records.create"];
        permissions.Permissions[actor.Id] = [];
        Assert.Equal(AiAssistantErrors.Forbidden, (await guard.AuthorizeAsync(context, Ct)).Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_inactive_actor_or_subject_cannot_execute_a_captured_channel(bool deactivateActor)
    {
        var users = new InMemoryUserAccounts(); var actor = users.AddUser("operator@example.test"); var subject = users.AddUser("recipient@example.test");
        var permissions = new FakePermissionService(); permissions.Permissions[actor.Id] = ["fictional.channel.read", "fictional.channel.send"];
        permissions.Permissions[subject.Id] = ["fictional.records.create"];
        await users.SetActiveAsync(deactivateActor ? actor.Id : subject.Id, false, Ct);
        Assert.Equal(AiAssistantErrors.Forbidden, (await Guard(users, permissions, actor.Id).AuthorizeAsync(Context(actor.Id, subject.Id), Ct)).Error);
    }

    [Fact]
    public async Task A_required_subject_is_never_inferred_from_the_request_scope_or_actor()
    {
        var users = new InMemoryUserAccounts(); var actor = users.AddUser("operator@example.test");
        var permissions = new FakePermissionService(); permissions.Permissions[actor.Id] = ["fictional.channel.read", "fictional.channel.send", "fictional.records.create"];
        var context = Context(actor.Id, actor.Id) with { SubjectUserId = null };
        Assert.Equal(AiAssistantErrors.Forbidden, (await Guard(users, permissions, actor.Id).AuthorizeAsync(context, Ct)).Error);
    }

    private static AssistantChannelContext Context(Guid actor, Guid subject) => new("owner-channel", actor, subject, "es", "UTC",
        new(["fictional.channel.read", "fictional.channel.send"], ["fictional.records.create"]), [new("fictional.propose", 1)], [new("assistant_proposal", 1)]);
    private static AiAssistantAccessGuard Guard(InMemoryUserAccounts users, FakePermissionService permissions, Guid ambientActor) =>
        new(users, permissions, [], new AiAssistantLimits(), new FakeCurrentUser { UserId = ambientActor },
            new AiAssistantCapabilityCatalog([], permissions), []);
}
