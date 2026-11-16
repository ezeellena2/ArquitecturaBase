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
    public void The_catalog_takes_the_sealed_version_only_of_a_capability_it_knows()
    {
        var f = new AssistantOperationsFixture();
        var raised = Raised(f.Context);
        var unknown = f.Context with { AllowedCapabilities = [new("fixture.proposal", 9)] };

        Assert.Equal(f.Context.AllowedCapabilities, f.Catalog.AsSealed(raised, f.Context).AllowedCapabilities);
        Assert.Equal(raised.AllowedCapabilities, f.Catalog.AsSealed(raised, unknown).AllowedCapabilities);
    }
}
