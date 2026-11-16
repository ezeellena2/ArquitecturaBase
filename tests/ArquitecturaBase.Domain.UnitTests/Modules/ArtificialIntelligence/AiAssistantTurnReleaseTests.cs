using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

// I12: a copy that stops gives back the turn it took and has not reserved a step for; the next claim fences the old copy out.
public sealed class AiAssistantTurnReleaseTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_turn_without_a_reserved_step_goes_back_to_pending_and_the_next_claim_fences_out_the_copy_that_stopped()
    {
        var turn = Claimed();
        var stopped = turn.Fence;

        Assert.True(turn.Release(stopped, Now.AddSeconds(5)).IsSuccess);

        Assert.Equal((AiAssistantTurnState.Pending, "pending", null, null, null), (turn.State, turn.Stage, turn.AttemptId, turn.DeadlineAtUtc, turn.FailureCode));
        Assert.Null(turn.CompletedAtUtc);
        Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(96)).IsSuccess);
        Assert.NotEqual(stopped, turn.Fence);
        Assert.True(turn.BeginProviderStep(stopped, Now.AddSeconds(7), 5).IsFailure);
        Assert.True(turn.Fail(stopped, AiAssistantErrors.Interrupted.Code, false, Now.AddSeconds(7)).IsFailure);
        Assert.Equal(AiAssistantTurnState.Running, turn.State);
    }

    [Fact]
    public void A_released_turn_still_expires_if_it_waited_longer_than_a_pending_turn_may()
    {
        var turn = Claimed();
        turn.Release(turn.Fence, Now.AddSeconds(5));

        turn.Reconcile(Now.AddSeconds(120), 120, 15);

        Assert.Equal((AiAssistantTurnState.Expired, AiAssistantErrors.Expired.Code), (turn.State, turn.FailureCode));
    }

    [Fact]
    public void A_turn_with_a_reserved_step_never_goes_back_to_pending_and_ends_failed_with_the_shutdown_code()
    {
        var turn = Claimed();
        turn.BeginProviderStep(turn.Fence, Now.AddSeconds(2), 5);

        Assert.True(turn.Release(turn.Fence, Now.AddSeconds(5)).IsSuccess);

        Assert.Equal((AiAssistantTurnState.Failed, AiAssistantErrors.Interrupted.Code), (turn.State, turn.FailureCode));
    }

    [Fact]
    public void A_canceled_or_expired_turn_closes_as_it_would_instead_of_going_back_to_pending()
    {
        var canceled = Claimed();
        canceled.RequestCancellation(Now.AddSeconds(2));
        canceled.Release(canceled.Fence, Now.AddSeconds(5));
        var expired = Claimed();
        expired.Release(expired.Fence, Now.AddSeconds(91));

        Assert.Equal(AiAssistantTurnState.Canceled, canceled.State);
        Assert.Equal((AiAssistantTurnState.Expired, AiAssistantErrors.Expired.Code), (expired.State, expired.FailureCode));
    }

    [Fact]
    public void Another_fence_or_a_finished_turn_is_rejected_without_changes()
    {
        var turn = Claimed();
        var revision = turn.Revision;

        Assert.Equal(AiAssistantErrors.FenceRejected, turn.Release(turn.Fence + 1, Now.AddSeconds(5)).Error);
        Assert.Equal((AiAssistantTurnState.Running, revision), (turn.State, turn.Revision));
        turn.Fail(turn.Fence, AiAssistantErrors.ExecutionFailed.Code, false, Now.AddSeconds(6));
        Assert.Equal(AiAssistantErrors.FenceRejected, turn.Release(turn.Fence, Now.AddSeconds(7)).Error);
        Assert.Equal(AiAssistantTurnState.Failed, turn.State);
    }

    private static AiAssistantTurn Claimed()
    {
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, Now, Now.AddDays(30));
        turn.Claim(Guid.NewGuid(), Now.AddSeconds(90));
        return turn;
    }
}
