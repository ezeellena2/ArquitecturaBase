using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I4: a deployment that raises the version of a module's capability keeps the earlier one registered. What was sealed with it is still
// found, replayed and cancelled with the context the channel builds now, while the catalog knows it; another capability is not.
public sealed class AiAssistantSealedVersionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The channel after the deployment: the same capability, one version up (a fictitious one).
    private static AssistantChannelContext Raised(AssistantChannelContext context) =>
        context with { AllowedCapabilities = context.AllowedCapabilities.Select(capability => capability with { Version = capability.Version + 1 }).ToArray() };

    [Fact]
    public async Task An_operation_sealed_with_an_earlier_version_is_found_replayed_and_cancelled_with_the_context_built_now()
    {
        var f = new AssistantOperationsFixture();
        var request = f.Request();
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        var raised = Raised(f.Context);

        var found = await f.Service.GetAsync(raised, request.RequestId, Ct);
        var replayed = await f.Service.SubmitAsync(request with { Context = raised }, Ct);
        var canceled = await f.Service.CancelAsync(raised, request.RequestId, Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Code : null);
        Assert.Equal((request.RequestId, "pending"), (found.Value.RequestId, found.Value.State));
        Assert.True(replayed.IsSuccess, replayed.IsFailure ? replayed.Error.Code : null);
        Assert.Equal(request.RequestId, replayed.Value.RequestId);
        Assert.True(canceled.IsSuccess);
        var turn = Assert.Single(f.Store.Turns);
        Assert.Equal(AiAssistantTurnState.Canceled, turn.State);
    }

    [Fact]
    public async Task A_completed_operation_sealed_with_an_earlier_version_returns_its_proposal_to_the_context_built_now()
    {
        var f = new AssistantOperationsFixture();
        var (turn, proposal, _) = f.SeedCompleted();

        var found = await f.Service.GetAsync(Raised(f.Context), turn.RequestId, Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Code : null);
        Assert.Equal(("completed", proposal.Id, proposal.Capability), (found.Value.State, found.Value.Proposal?.Id, found.Value.Proposal?.Capability));
    }

    [Fact]
    public async Task Another_capability_is_not_the_sealed_one_so_the_scope_and_the_replay_still_refuse_it()
    {
        var f = new AssistantOperationsFixture();
        var request = f.Request();
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        var other = f.Context with { AllowedCapabilities = [new("fixture.other", 1)] };

        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.GetAsync(other, request.RequestId, Ct)).Error);
        Assert.Equal(AiAssistantErrors.RequestReused, (await f.Service.SubmitAsync(request with { Context = other }, Ct)).Error);
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.CancelAsync(other, request.RequestId, Ct)).Error);
    }

    [Fact]
    public void The_catalog_takes_the_sealed_version_only_of_a_capability_or_a_terminal_it_knows()
    {
        var f = new AssistantOperationsFixture();
        var raised = Raised(f.Context) with { AllowedTerminals = [new("assistant_proposal", 2)] };
        var unknown = f.Context with { AllowedCapabilities = [new("fixture.proposal", 9)], AllowedTerminals = [new("assistant_proposal", 9)] };

        Assert.Equal(f.Context.AllowedCapabilities, f.Catalog.AsSealed(raised, f.Context).AllowedCapabilities);
        Assert.Equal(f.Context.AllowedTerminals, f.Catalog.AsSealed(raised, f.Context).AllowedTerminals);
        Assert.Equal(raised.AllowedCapabilities, f.Catalog.AsSealed(raised, unknown).AllowedCapabilities);
        Assert.Equal(raised.AllowedTerminals, f.Catalog.AsSealed(raised, unknown).AllowedTerminals);
    }

    // I4, review: a channel that raises its terminal (assistant_proposal@1 to @2) still finds, replays and cancels what the earlier one
    // sealed, as with a capability.
    [Fact]
    public async Task An_operation_sealed_with_an_earlier_terminal_is_found_replayed_and_cancelled_after_raising_it()
    {
        var f = new AssistantOperationsFixture();
        var request = f.Request();
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        var raised = f.Context with { AllowedTerminals = [new("assistant_proposal", 2)] };

        var found = await f.Service.GetAsync(raised, request.RequestId, Ct);
        var replayed = await f.Service.SubmitAsync(request with { Context = raised }, Ct);
        var canceled = await f.Service.CancelAsync(raised, request.RequestId, Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Code : null);
        Assert.True(replayed.IsSuccess, replayed.IsFailure ? replayed.Error.Code : null);
        Assert.Equal(request.RequestId, replayed.Value.RequestId);
        Assert.True(canceled.IsSuccess);
        Assert.Equal(AiAssistantTurnState.Canceled, Assert.Single(f.Store.Turns).State);
    }

    // I4, review: the fingerprint has every capability the channel asked, also one the seal left out because the catalog filtered it (here,
    // by a permission nobody has). After raising that one, the replay tries the versions its Id still registers.
    [Fact]
    public async Task The_replay_matches_a_capability_asked_but_left_out_of_the_seal_after_raising_it()
    {
        var gated = new RoundtripAssistantSource().Definitions[0] with { Id = "fixture.gated", RequiredPermissions = new(["fictional.missing.permission"], []) };
        var f = new AssistantOperationsFixture(extraCapabilities: [gated, gated with { Version = 2 }]);
        var request = f.Request() with { Context = f.Context with { AllowedCapabilities = [.. f.Context.AllowedCapabilities, new(gated.Id, 1)] } };
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal(f.Context.AllowedCapabilities, f.Guard.ReadSeal(Assert.Single(f.Store.Turns).ExecutionProfileJson).Value.ChannelContext.AllowedCapabilities);
        var raised = request.Context with { AllowedCapabilities = [.. f.Context.AllowedCapabilities, new(gated.Id, 2)] };

        var replayed = await f.Service.SubmitAsync(request with { Context = raised }, Ct);
        var other = await f.Service.SubmitAsync(request with { Context = request.Context with { AllowedCapabilities = [.. f.Context.AllowedCapabilities, new("fixture.other", 1)] } }, Ct);

        Assert.True(replayed.IsSuccess, replayed.IsFailure ? replayed.Error.Code : null);
        Assert.Equal(request.RequestId, replayed.Value.RequestId);
        Assert.Equal(AiAssistantErrors.RequestReused, other.Error);
        Assert.Single(f.Store.Turns);
    }
}
