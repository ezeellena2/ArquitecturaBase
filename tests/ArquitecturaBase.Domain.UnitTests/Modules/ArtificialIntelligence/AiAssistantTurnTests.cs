using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantTurnTests
{
    [Fact]
    public void A_confirmed_cancel_request_prevents_late_completion()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddDays(30));
        Assert.True(turn.Claim(Guid.NewGuid(), now.AddSeconds(90)).IsSuccess);
        var fence = turn.Fence;
        turn.RequestCancellation(now.AddSeconds(1));
        Assert.True(turn.Complete(fence, "answer", "evidence", now.AddSeconds(2)).IsFailure);
        Assert.NotEqual(AiAssistantTurnState.Completed, turn.State);
    }

    [Fact]
    public void A_late_worker_cannot_record_another_provider_step()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "en", "UTC",
            AiProvider.Anthropic, "model", 1, now, now.AddDays(30));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        Assert.True(turn.BeginProviderStep(turn.Fence + 1, now, 4).IsFailure);
        Assert.Equal(0, turn.ProviderSteps);
        Assert.True(turn.BeginProviderStep(turn.Fence, now.AddSeconds(91), 4).IsFailure);
    }

    [Fact]
    public void An_expired_running_turn_with_a_reserved_step_becomes_uncertain_without_reclaim()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddDays(30));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        turn.BeginProviderStep(turn.Fence, now, 4);
        turn.Reconcile(now.AddSeconds(106), 120, 15);
        Assert.Equal(AiAssistantTurnState.Uncertain, turn.State);
        Assert.True(turn.Claim(Guid.NewGuid(), now.AddSeconds(200)).IsFailure);
    }

    [Fact]
    public void Expiration_before_any_provider_step_is_not_reported_as_a_provider_failure()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddDays(30));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        turn.Fail(turn.Fence, AiAssistantErrors.Expired.Code, false, now.AddSeconds(91));
        Assert.Equal(AiAssistantTurnState.Expired, turn.State);
    }

    [Fact]
    public void Missing_usage_in_an_earlier_step_remains_unknown_after_a_known_step()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddDays(30));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        turn.BeginProviderStep(turn.Fence, now, 4);
        turn.RecordProgress(turn.Fence, now, "composing", 0, 0, null, null);
        turn.BeginProviderStep(turn.Fence, now, 4);
        turn.RecordProgress(turn.Fence, now, "verifying", 0, 0, 10, 5);
        Assert.True(turn.UsageUnknown);
        Assert.Equal(10, turn.InputTokens);
        Assert.Equal(5, turn.OutputTokens);
    }

    [Fact]
    public void A_pending_turn_seals_a_profile_at_the_UTF8_byte_limit()
    {
        var turn = NewTurn();
        var profile = new string('é', 8192);
        Assert.True(turn.SealExecutionProfile(profile).IsSuccess);
        Assert.Equal(profile, turn.ExecutionProfileJson);
        Assert.Equal(AiAssistantTurnState.Pending, turn.State);
    }

    [Fact]
    public void An_empty_profile_is_invalid_and_does_not_mutate_the_turn()
    {
        var turn = NewTurn();
        var revision = turn.Revision;
        Assert.Equal(AiAssistantErrors.Invalid, turn.SealExecutionProfile(string.Empty).Error);
        Assert.Equal(AiAssistantErrors.Invalid, turn.SealExecutionProfile(" ").Error);
        Assert.Null(turn.ExecutionProfileJson);
        Assert.Equal(revision, turn.Revision);
    }

    [Fact]
    public void An_oversized_profile_is_rejected_by_UTF8_bytes_without_mutation()
    {
        var turn = NewTurn();
        var revision = turn.Revision;
        Assert.Equal(AiAssistantErrors.LimitExceeded, turn.SealExecutionProfile(new string('é', 8193)).Error);
        Assert.Null(turn.ExecutionProfileJson);
        Assert.Equal(revision, turn.Revision);
    }

    [Fact]
    public void A_sealed_profile_allows_identical_replay_and_rejects_replacement_without_mutation()
    {
        var turn = NewTurn();
        const string profile = "{\"modelProfileVersion\":1}";
        Assert.True(turn.SealExecutionProfile(profile).IsSuccess);
        var revision = turn.Revision;
        Assert.True(turn.SealExecutionProfile(profile).IsSuccess);
        Assert.Equal(AiAssistantErrors.RevisionConflict, turn.SealExecutionProfile("{\"modelProfileVersion\":2}").Error);
        Assert.Equal(profile, turn.ExecutionProfileJson);
        Assert.Equal(revision, turn.Revision);
    }

    [Fact]
    public void A_claimed_turn_cannot_capture_or_replace_its_execution_profile()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var unsealed = NewTurn();
        Assert.True(unsealed.Claim(Guid.NewGuid(), now.AddSeconds(90)).IsSuccess);
        var unsealedRevision = unsealed.Revision;
        Assert.Equal(AiAssistantErrors.RevisionConflict, unsealed.SealExecutionProfile("profile").Error);
        Assert.Null(unsealed.ExecutionProfileJson);
        Assert.Equal(unsealedRevision, unsealed.Revision);

        var sealedTurn = NewTurn();
        Assert.True(sealedTurn.SealExecutionProfile("original").IsSuccess);
        Assert.True(sealedTurn.Claim(Guid.NewGuid(), now.AddSeconds(90)).IsSuccess);
        var sealedRevision = sealedTurn.Revision;
        Assert.Equal(AiAssistantErrors.RevisionConflict, sealedTurn.SealExecutionProfile("replacement").Error);
        Assert.True(sealedTurn.SealExecutionProfile("original").IsSuccess);
        Assert.Equal("original", sealedTurn.ExecutionProfileJson);
        Assert.Equal(sealedRevision, sealedTurn.Revision);
    }

    private static AiAssistantTurn NewTurn()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        return AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "es", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddDays(30));
    }
}
