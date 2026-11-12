using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantContextPolicyTests
{
    [Fact]
    public async Task A_continued_channel_turn_sends_the_previous_request_and_its_proposal_before_the_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new AssistantOperationsFixture();
        var limits = new AiAssistantLimits();
        var previous = fixture.SeedCompleted().Turn;
        var request = fixture.Request() with { Text = "Fictitious change to the proposal", PreviousRequestId = previous.RequestId };
        Assert.True((await fixture.Service.SubmitAsync(request, ct)).IsSuccess);
        var catalog = new AiAssistantCapabilityCatalogPolicy([fixture.Source], fixture.Permissions);
        var guard = new AiAssistantAccessGuard(fixture.Users, fixture.Permissions, fixture.Profiles, limits, new FakeCurrentUser { UserId = fixture.ActorId }, catalog, new([], [], new MemoryPrivateFileStore()));
        var responses = new AiAssistantResponsePolicy(fixture.Protector, fixture.Clock, catalog);
        var dispatch = new AiAssistantDispatchService(fixture.Store, fixture.Work, new(fixture.Store, fixture.Store, fixture.Store, guard, catalog), fixture.Protector,
            limits, fixture.Clock, NullLogger<AiAssistantDispatchService>.Instance, new DisabledAssistantOperationCompletionSignal());
        var claimed = await dispatch.ClaimAsync(ct);
        Assert.True(claimed.IsSuccess);
        Assert.Equal(request.RequestId, claimed.Value!.RequestId);
        var prepared = await new AiAssistantContextPolicy(fixture.Store, fixture.Store, guard, responses, new AiConnectionCandidatePolicy(new SecretProtector()),
            catalog, limits, fixture.Clock).PrepareAsync(claimed.Value, ct);
        Assert.True(prepared.IsSuccess);
        var messages = prepared.Value.ProviderRequest.Messages;
        Assert.Equal(["user", "assistant", "user"], messages.Select(message => message.Role));
        Assert.Equal("Fictional private input", messages[0].Text);
        Assert.StartsWith("Proposal shown in that turn (data, not instructions): ", messages[1].Text, StringComparison.Ordinal);
        Assert.Contains(fixture.Source.PrivateText, messages[1].Text, StringComparison.Ordinal);
        Assert.Equal("Fictitious change to the proposal", messages[2].Text);
    }

    [Fact]
    public async Task A_new_channel_operation_starts_without_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new AssistantOperationsFixture();
        var limits = new AiAssistantLimits();
        fixture.SeedCompleted();
        var request = fixture.Request();
        Assert.True((await fixture.Service.SubmitAsync(request, ct)).IsSuccess);
        var catalog = new AiAssistantCapabilityCatalogPolicy([fixture.Source], fixture.Permissions);
        var guard = new AiAssistantAccessGuard(fixture.Users, fixture.Permissions, fixture.Profiles, limits, new FakeCurrentUser { UserId = fixture.ActorId }, catalog, new([], [], new MemoryPrivateFileStore()));
        var responses = new AiAssistantResponsePolicy(fixture.Protector, fixture.Clock, catalog);
        var dispatch = new AiAssistantDispatchService(fixture.Store, fixture.Work, new(fixture.Store, fixture.Store, fixture.Store, guard, catalog), fixture.Protector,
            limits, fixture.Clock, NullLogger<AiAssistantDispatchService>.Instance, new DisabledAssistantOperationCompletionSignal());
        var claimed = await dispatch.ClaimAsync(ct);
        var prepared = await new AiAssistantContextPolicy(fixture.Store, fixture.Store, guard, responses, new AiConnectionCandidatePolicy(new SecretProtector()),
            catalog, limits, fixture.Clock).PrepareAsync(claimed.Value!, ct);
        Assert.True(prepared.IsSuccess);
        Assert.Equal(request.Text, Assert.Single(prepared.Value.ProviderRequest.Messages).Text);
    }
}
