using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// ADR 0021, point 8: a capability whose proposal carries text the model wrote is offered only with a model profile qualified to draft.
public sealed class AiAssistantDraftQualificationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AiConnectionRow Connection = new(AiProvider.OpenAI, "model", "cipher-key", true, 1);

    private sealed record Host(AiAssistantAccessGuard Guard, AssistantChannelContext Channel);

    private static Host Create(bool qualified)
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("draft-qualification-fiction@example.test");
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [];
        var source = new PreparedAssistantCapabilitySource([
            PreparedAssistantCapabilitySource.Definition("fictional.exact-reply", 1, AssistantCapabilityKind.Proposal),
            PreparedAssistantCapabilitySource.Definition("fictional.drafted-reply", 1, AssistantCapabilityKind.Proposal) with { DraftsText = true }]);
        var catalog = new AiAssistantCapabilityCatalogPolicy([source], permissions);
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(users, permissions,
            [new AiAssistantModelProfile(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "draft-fixture", Version = 1, QualifiedToDraft = qualified }],
            limits, new FakeCurrentUser { UserId = actor.Id }, catalog, new([], [], new MemoryPrivateFileStore()));
        var channel = new AssistantChannelContext("fixture-draft", actor.Id, actor.Id, "es", "UTC", new([], []),
            [new("fictional.exact-reply", 1), new("fictional.drafted-reply", 1)], [new("assistant_proposal", 2)])
            { InteractionMode = AssistantInteractionMode.Assistant };
        return new(guard, channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_capability_that_drafts_text_is_sealed_only_with_a_profile_qualified_to_draft(bool qualified)
    {
        var host = Create(qualified);
        var captured = await host.Guard.CaptureOperationProfileAsync(Connection, host.Channel, DateTime.UnixEpoch, AssistantAttachmentKind.None, Ct);
        Assert.True(captured.IsSuccess);
        Assert.Equal(qualified ? new[] { "fictional.exact-reply", "fictional.drafted-reply" } : ["fictional.exact-reply"],
            captured.Value.ChannelContext.AllowedCapabilities.Select(capability => capability.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_sealed_drafting_capability_needs_its_profile_still_qualified(bool qualified)
    {
        var host = Create(qualified);
        var sealedProfile = new AiAssistantExecutionProfileSnapshot("draft-fixture", 1, "capabilities-v1", 1, host.Channel);
        Assert.Equal(qualified, host.Guard.IsProfileQualified(sealedProfile, Connection));
        var exactOnly = sealedProfile with { ChannelContext = host.Channel with { AllowedCapabilities = [new("fictional.exact-reply", 1)] } };
        Assert.True(host.Guard.IsProfileQualified(exactOnly, Connection));
    }
}
