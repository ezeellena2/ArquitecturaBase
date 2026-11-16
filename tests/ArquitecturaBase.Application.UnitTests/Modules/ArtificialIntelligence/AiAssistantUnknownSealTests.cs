using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.Assistant;
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
            case "preload": context["preloadedCapabilities"] = new JsonArray(new JsonObject { ["id"] = "fixture.proposal", ["version"] = 9 }); break;
        }
        Reseal(turn, seal.ToJsonString());
    }

    // I4, review: what no version writes (a negative value, empty instructions, a null terminal or capability) and what this version
    // retired or never had (an Id it does not register) are not newer: they close or deny as before.
    public static TheoryData<string> RetiredOrDamagedValues => ["purpose", "attachment", "mode", "instructions-empty", "instructions-null",
        "instructions-version", "instructions-removed", "terminal-null", "terminal-version", "terminal-removed", "capability-null",
        "capability-removed", "preload-removed"];

    private static void SealRetiredOrDamaged(AiAssistantTurn turn, string value)
    {
        var seal = JsonNode.Parse(turn.ExecutionProfileJson!)!.AsObject();
        var context = seal["channelContext"]!.AsObject();
        switch (value)
        {
            case "purpose": seal["purpose"] = -1; break;
            case "attachment": seal["attachment"] = -1; break;
            case "mode": context["interactionMode"] = -1; break;
            case "instructions-empty": seal["instructionsId"] = ""; break;
            case "instructions-null": seal.Remove("instructionsId"); break;
            case "instructions-version": seal["instructionsVersion"] = 0; break;
            case "instructions-removed": seal["instructionsId"] = "fixture-retired"; break;
            case "terminal-null": context["allowedTerminals"] = new JsonArray((JsonNode?)null); break;
            case "terminal-version": context["allowedTerminals"] = new JsonArray(new JsonObject { ["id"] = "assistant_proposal", ["version"] = 0 }); break;
            case "terminal-removed": context["allowedTerminals"] = new JsonArray(new JsonObject { ["id"] = "fixture_retired", ["version"] = 1 }); break;
            case "capability-null": context["allowedCapabilities"] = new JsonArray((JsonNode?)null); break;
            case "capability-removed": context["allowedCapabilities"]![0]!["id"] = "fixture.retired"; break;
            case "preload-removed": context["preloadedCapabilities"] = new JsonArray(new JsonObject { ["id"] = "fixture.retired", ["version"] = 1 }); break;
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
        // I4, review: one query of the pending turns in order, chosen in memory, not one per skipped turn under the installation's lock.
        var queries = f.Store.PendingQueries;
        var next = await dispatch.ClaimAsync(Ct);

        Assert.Equal(1, queries);
        Assert.Equal(known.Id, claimed.Value?.TurnId);
        Assert.Equal(AiAssistantTurnState.Running, known.State);
        Assert.True(next.IsSuccess);
        Assert.Null(next.Value);
        Assert.Equal((AiAssistantTurnState.Pending, (string?)null, 0), (future.State, future.FailureCode, future.ProviderSteps));
    }

    // I4, review: the cancellation is recorded without the seal, after the channel and the person in clear, so a copy that understands the
    // turn never runs one already cancelled; it reveals nothing.
    [Theory]
    [MemberData(nameof(FutureValues))]
    public async Task Getting_an_operation_with_a_future_value_answers_that_it_may_pass_and_canceling_it_is_recorded(string value)
    {
        var f = new AssistantOperationsFixture();
        var requestId = Guid.NewGuid();
        var turn = await SubmitAsync(f, requestId);
        SealFuture(turn, value);
        var other = f.Context with { ChannelId = "fixture-other" };

        // Another channel still does not learn that it exists, nor cancels it.
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.GetAsync(other, requestId, Ct)).Error);
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.CancelAsync(other, requestId, Ct)).Error);
        var found = await f.Service.GetAsync(f.Context, requestId, Ct);
        var canceled = await f.Service.CancelAsync(f.Context, requestId, Ct);

        Assert.Equal(AiAssistantErrors.UnknownVersion, found.Error);
        Assert.True(canceled.IsSuccess, canceled.IsFailure ? canceled.Error.Code : null);
        Assert.Equal((AiAssistantTurnState.Canceled, AiAssistantErrors.Canceled.Code), (turn.State, turn.FailureCode));
    }

    // I4, review: a finished operation has nothing left for the version that knows the seal, so its state and failure go in clear, after the
    // channel and the person; otherwise a channel would hear «it may pass» until the receipt expires.
    public static TheoryData<string> FinishedStates => ["expired", "failed", "canceled"];

    [Theory]
    [MemberData(nameof(FinishedStates))]
    public async Task A_finished_operation_with_a_future_value_answers_its_state_in_clear(string state)
    {
        var f = new AssistantOperationsFixture();
        var requestId = Guid.NewGuid();
        var turn = await SubmitAsync(f, requestId);
        SealFuture(turn, "terminal");
        var now = f.Clock.GetUtcNow().UtcDateTime;
        switch (state)
        {
            case "expired": turn.Reconcile(now.AddSeconds(f.Limits.PendingSeconds), f.Limits.PendingSeconds, f.Limits.LeaseGraceSeconds); break;
            case "failed":
                Assert.True(turn.Claim(Guid.NewGuid(), now.AddSeconds(f.Limits.RunningSeconds)).IsSuccess);
                turn.Fail(turn.Fence, AiAssistantErrors.Unavailable.Code, false, now);
                break;
            case "canceled": turn.RequestCancellation(now); break;
        }

        var found = await f.Service.GetAsync(f.Context, requestId, Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Code : null);
        Assert.NotNull(turn.FailureCode);
        Assert.Equal((requestId, state, turn.FailureCode), (found.Value.RequestId, found.Value.State, found.Value.FailureCode));
        Assert.Null(found.Value.Provenance);
        Assert.Equal(0, f.Protector.Reads);
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.GetAsync(f.Context with { ChannelId = "fixture-other" }, requestId, Ct)).Error);
    }

    [Theory]
    [MemberData(nameof(RetiredOrDamagedValues))]
    public async Task A_retired_or_damaged_value_is_unavailable_content_and_the_claim_closes_the_turn(string value)
    {
        var f = new AssistantOperationsFixture();
        var requestId = Guid.NewGuid();
        var turn = await SubmitAsync(f, requestId);
        SealRetiredOrDamaged(turn, value);

        Assert.Equal(AiAssistantErrors.ContentUnavailable, f.Guard.ReadSeal(turn.ExecutionProfileJson).Error);
        Assert.Equal(AiAssistantErrors.NotFound, (await f.Service.GetAsync(f.Context, requestId, Ct)).Error);
        Assert.Null((await Dispatch(f).ClaimAsync(Ct)).Value);
        Assert.Equal(AiAssistantTurnState.Failed, turn.State);
    }

    // A module keeps an earlier version registered while work sealed with it lasts: a version above the highest it registers is newer, and
    // one below that it no longer registers was retired.
    [Fact]
    public void A_capability_version_is_newer_only_above_the_highest_registered_for_its_id()
    {
        var f = new AssistantOperationsFixture();
        var definition = f.Source.Definitions[0];
        var catalog = new AiAssistantCapabilityCatalogPolicy(
            [new PreparedAssistantCapabilitySource([definition with { Version = 2 }, definition with { Version = 4 }])], f.Permissions);
        AssistantChannelContext Sealed(int version) => f.Context with { AllowedCapabilities = [new(definition.Id, version)] };

        Assert.Equal([null, null, AiAssistantErrors.UnknownVersion, AiAssistantErrors.ContentUnavailable, AiAssistantErrors.ContentUnavailable],
            new[] { catalog.Classify(Sealed(2)), catalog.Classify(Sealed(4)), catalog.Classify(Sealed(5)), catalog.Classify(Sealed(3)), catalog.Classify(Sealed(1)) });
    }

    // I4, review: continuing an operation whose seal this version does not know answers that it may pass, not that it does not exist.
    [Theory]
    [InlineData("purpose")]
    [InlineData("instructions")]
    [InlineData("terminal")]
    public async Task Continuing_an_operation_with_a_future_value_answers_that_it_may_pass(string value)
    {
        var f = new AssistantOperationsFixture();
        var (turn, _, _) = f.SeedCompleted();
        SealFuture(turn, value);

        var continued = await f.Service.SubmitAsync(f.Request(Guid.NewGuid()) with { PreviousRequestId = turn.RequestId }, Ct);

        Assert.Equal(AiAssistantErrors.UnknownVersion, continued.Error);
        Assert.Single(f.Store.Turns);
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
