using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I12: the copy that stops gives back, in one commit, the turn it took and has not reserved a step for.
public sealed partial class AiAssistantDispatchServiceTests
{
    [Fact]
    public async Task Releasing_a_turn_without_a_step_makes_it_pending_again_without_waking_the_channel()
    {
        var host = new NeutralHost(false);

        Assert.True((await host.Service.ReleaseAsync(host.Claim, Ct)).IsSuccess);

        Assert.Equal((AiAssistantTurnState.Pending, null), (host.Store.Turn.State, host.Store.Turn.FailureCode));
        Assert.Equal(1, host.Work.Commits);
        Assert.Empty(host.CompletionSignal.Channels);
    }

    [Fact]
    public async Task Releasing_a_turn_that_reserved_a_step_closes_it_with_the_shutdown_code_and_wakes_the_channel()
    {
        var host = new NeutralHost(false);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);

        Assert.True((await host.Service.ReleaseAsync(host.Claim, Ct)).IsSuccess);

        Assert.Equal((AiAssistantTurnState.Failed, AiAssistantErrors.Interrupted.Code), (host.Store.Turn.State, host.Store.Turn.FailureCode));
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    [Fact]
    public async Task Another_fence_releases_nothing()
    {
        var host = new NeutralHost(false);

        var result = await host.Service.ReleaseAsync(host.Claim with { Fence = host.Claim.Fence + 1 }, Ct);

        Assert.Equal(AiAssistantErrors.FenceRejected, result.Error);
        Assert.Equal(AiAssistantTurnState.Running, host.Store.Turn.State);
        Assert.Empty(host.CompletionSignal.Channels);
    }
}
