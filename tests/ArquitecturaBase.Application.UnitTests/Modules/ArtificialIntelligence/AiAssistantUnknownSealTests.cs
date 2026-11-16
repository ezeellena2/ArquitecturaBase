using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I4: a value of the seal that this version does not know was written by a newer one. It is neither damaged nor absent: the claim leaves it
// for the version that knows it, and the lookups answer a failure that may pass instead of «not found».
public sealed class AiAssistantUnknownSealTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> FutureValues => ["purpose", "attachment", "mode", "instructions", "terminal", "capability", "preload"];

    private static void SealFuture(AiAssistantTurn turn, string value)
    {
        var seal = JsonNode.Parse(turn.ExecutionProfileJson!)!.AsObject();
        var context = seal["channelContext"]!.AsObject();
        switch (value)
        {
            case "purpose": seal["purpose"] = 9; break;
            case "attachment": seal["attachment"] = 9; break;
            case "mode": context["interactionMode"] = 9; break;
            case "instructions": seal["instructionsVersion"] = 2; break;
            case "terminal": context["allowedTerminals"] = new JsonArray(new JsonObject { ["id"] = "assistant_proposal", ["version"] = 9 }); break;
            case "capability": context["allowedCapabilities"]![0]!["version"] = 9; break;
            case "preload": context["preloadedCapabilities"] = new JsonArray(new JsonObject { ["id"] = "fixture.future-read", ["version"] = 1 }); break;
        }
        Reseal(turn, seal.ToJsonString());
    }

    // The seal is written once by the engine; a test stands in for the newer version that wrote another one.
    private static void Reseal(AiAssistantTurn turn, string json) =>
        typeof(AiAssistantTurn).GetProperty(nameof(AiAssistantTurn.ExecutionProfileJson))!.SetValue(turn, json);

    private static async Task<AiAssistantTurn> SubmitAsync(AssistantOperationsFixture f, Guid requestId, Guid? channelConversationId = null)
    {
        var request = f.Request(requestId) with { Context = f.Context with { ChannelConversationId = channelConversationId } };
        var accepted = await f.Service.SubmitAsync(request, Ct);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        return f.Store.Turns.Single(turn => turn.RequestId == requestId);
    }

    private static AiAssistantDispatchService Dispatch(AssistantOperationsFixture f) => new(f.Store, f.Work,
        new AiAssistantDispatchGuard(f.Store, f.Store, f.Store, f.Guard, f.Catalog), f.Protector, f.Limits, f.Clock,
        NullLogger<AiAssistantDispatchService>.Instance, new RecordingAssistantOperationCompletionSignal());

    [Fact]
    public void The_failure_of_an_unknown_value_may_pass_like_an_active_turn()
    {
        Assert.Equal((ErrorType.Conflict, true), (AiAssistantErrors.UnknownVersion.Type, AiAssistantErrors.UnknownVersion.Metadata?["retryable"]));
        Assert.NotEqual(AiAssistantErrors.NotFound.Type, AiAssistantErrors.UnknownVersion.Type);
    }

    [Theory]
    [MemberData(nameof(FutureValues))]
    public async Task A_value_this_version_does_not_know_is_told_apart_from_a_damaged_seal(string value)
    {
        var f = new AssistantOperationsFixture();
        var turn = await SubmitAsync(f, Guid.NewGuid());
        var sound = turn.ExecutionProfileJson!;
        SealFuture(turn, value);

        var read = f.Guard.ReadSeal(turn.ExecutionProfileJson);

        Assert.Equal(AiAssistantErrors.UnknownVersion, read.Error);
        Assert.True(f.Guard.ReadSeal(sound).IsSuccess);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"modelProfileId":"fixture-profile","modelProfileVersion":3,"instructionsId":"capabilities-v1","instructionsVersion":1}""")]
    [InlineData("""{"modelProfileId":"fixture-profile","modelProfileVersion":0,"instructionsId":"capabilities-v1","instructionsVersion":1,"channelContext":{"channelId":"fixture","actorUserId":"6a4b0d8e-0000-7000-8000-000000000001","culture":"en","timeZoneId":"UTC","requiredPermissions":{"actor":[],"subject":[]},"allowedCapabilities":[],"allowedTerminals":[{"id":"assistant_proposal","version":1}]}}""")]
    [InlineData("""{"modelProfileId":"fixture-audio","modelProfileVersion":2,"instructionsId":"audio-none-v1","instructionsVersion":1,"purpose":1,"channelContext":{"channelId":"fixture","actorUserId":"6a4b0d8e-0000-7000-8000-000000000001","culture":"en","timeZoneId":"UTC","requiredPermissions":{"actor":[],"subject":[]},"allowedCapabilities":[],"allowedTerminals":[{"id":"assistant_proposal","version":1}]}}""")]
    public void A_damaged_seal_is_still_unavailable_content(string json) =>
        Assert.Equal(AiAssistantErrors.ContentUnavailable, AiAssistantChannelPolicy.Read(json).Error);

    [Theory]
    [MemberData(nameof(FutureValues))]
    public async Task The_claim_leaves_a_turn_with_a_future_value_pending_and_takes_the_next_one(string value)
    {
        var f = new AssistantOperationsFixture();
        var future = await SubmitAsync(f, Guid.NewGuid());
        SealFuture(future, value);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var known = await SubmitAsync(f, Guid.NewGuid(), Guid.NewGuid());
        var dispatch = Dispatch(f);

        var claimed = await dispatch.ClaimAsync(Ct);
        var next = await dispatch.ClaimAsync(Ct);

        Assert.Equal(known.Id, claimed.Value?.TurnId);
        Assert.Equal(AiAssistantTurnState.Running, known.State);
        Assert.True(next.IsSuccess);
        Assert.Null(next.Value);
        Assert.Equal((AiAssistantTurnState.Pending, (string?)null, 0), (future.State, future.FailureCode, future.ProviderSteps));
    }

    [Theory]
    [MemberData(nameof(FutureValues))]
    public async Task Getting_or_canceling_an_operation_with_a_future_value_answers_that_it_may_pass_not_that_it_does_not_exist(string value)
    {
        var f = new AssistantOperationsFixture();
        var requestId = Guid.NewGuid();
        var turn = await SubmitAsync(f, requestId);
        SealFuture(turn, value);

        var found = await f.Service.GetAsync(f.Context, requestId, Ct);
        var canceled = await f.Service.CancelAsync(f.Context, requestId, Ct);

        Assert.Equal(AiAssistantErrors.UnknownVersion, found.Error);
        Assert.Equal(AiAssistantErrors.UnknownVersion, canceled.Error);
        Assert.Equal(AiAssistantTurnState.Pending, turn.State);
        // Another channel still does not learn that it exists.
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.GetAsync(f.Context with { ChannelId = "fixture-other" }, requestId, Ct)).Error);
    }

    [Theory]
    [MemberData(nameof(FutureValues))]
    public async Task Looking_up_a_proposal_of_a_turn_with_a_future_value_answers_that_it_may_pass(string value)
    {
        var f = new AssistantOperationsFixture();
        var (turn, proposal, _) = f.SeedCompleted();
        SealFuture(turn, value);
        var lookup = new AiAssistantProposalLookup(f.Store, f.Guard, f.Responses, NullLogger<AiAssistantProposalLookup>.Instance);

        var found = await lookup.FindAsync(f.ActorId, turn.RequestId, proposal.Id, ["fixture.proposal"], Ct);

        Assert.Equal(AiAssistantErrors.UnknownVersion, found.Error);
        Assert.Equal(0, f.Protector.Reads);
    }

    // A seal no version can read closes the turn as unavailable content, never as lost permissions that invite deleting the conversation.
    [Fact]
    public async Task A_damaged_seal_does_not_close_the_turn_as_lost_permissions()
    {
        var f = new AssistantOperationsFixture();
        var turn = await SubmitAsync(f, Guid.NewGuid());
        Reseal(turn, JsonSerializer.Serialize(new { modelProfileId = "fixture-profile" }));

        var claimed = await Dispatch(f).ClaimAsync(Ct);

        Assert.Null(claimed.Value);
        Assert.Equal((AiAssistantTurnState.Failed, AiAssistantErrors.ContentUnavailable.Code), (turn.State, turn.FailureCode));
    }
}
