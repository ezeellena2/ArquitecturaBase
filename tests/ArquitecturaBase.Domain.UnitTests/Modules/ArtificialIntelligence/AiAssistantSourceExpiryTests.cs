using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

// Uses the existing turn/expiry/fence model; no new persistence or retention model.
public sealed class AiAssistantSourceExpiryTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static AiAssistantTurn Turn() => AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        "protected-input", "es", "UTC", AiProvider.OpenAI, "fictitious-text-model", 4, Now, Now.AddSeconds(10));

    [Fact]
    public void Source_expiration_blocks_a_provider_step_even_when_the_attempt_deadline_is_later()
    {
        var turn = Turn();
        Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
        var revision = turn.Revision;
        var result = turn.BeginProviderStep(turn.Fence, Now.AddSeconds(10), 4);
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.Expired, result.Error);
        Assert.Equal(0, turn.ProviderSteps);
        Assert.Equal(revision, turn.Revision);
    }

    [Fact]
    public void Source_expiration_blocks_progress_and_does_not_record_late_content_or_usage()
    {
        var turn = Turn();
        Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
        Assert.True(turn.BeginProviderStep(turn.Fence, Now, 4).IsSuccess);
        var revision = turn.Revision;
        var result = turn.RecordProgress(turn.Fence, Now.AddSeconds(10), "composing", 1, 20, 12, 4);
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.Expired, result.Error);
        Assert.Equal(0, turn.ToolCalls);
        Assert.Equal(0, turn.ToolBytes);
        Assert.Null(turn.InputTokens);
        Assert.Null(turn.OutputTokens);
        Assert.Equal(revision, turn.Revision);
    }

    [Fact]
    public void A_late_known_result_cannot_repopulate_a_purged_turn_after_source_expiration()
    {
        var turn = Turn();
        Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
        Assert.True(turn.BeginProviderStep(turn.Fence, Now, 4).IsSuccess);
        turn.PurgeContent();
        var revision = turn.Revision;
        var result = turn.Complete(turn.Fence, "protected-result", "protected-metadata", Now.AddSeconds(11));
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.Expired, result.Error);
        Assert.Null(turn.ProtectedQuestion);
        Assert.Null(turn.ProtectedAnswer);
        Assert.Null(turn.ProtectedEvidences);
        Assert.NotEqual(AiAssistantTurnState.Completed, turn.State);
        Assert.Equal(revision, turn.Revision);
    }
}
