using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioUsageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2.75)]
    public void Reported_audio_duration_is_preserved_without_inventing_tokens(decimal seconds)
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "en", "UTC",
            AiProvider.OpenAI, "audio-model", 1, now, now.AddHours(1));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        turn.BeginProviderStep(turn.Fence, now, 4);
        turn.RecordUsage(null, null, seconds);
        Assert.Equal(seconds, turn.AudioSeconds);
        Assert.Null(turn.InputTokens);
        Assert.Null(turn.OutputTokens);
        Assert.True(turn.UsageUnknown);
    }

    [Fact]
    public void Known_duration_survives_a_late_failure_and_content_purge()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var turn = AiAssistantTurn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "cipher", "en", "UTC",
            AiProvider.OpenAI, "audio-model", 1, now, now.AddSeconds(1));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        turn.BeginProviderStep(turn.Fence, now, 4);
        turn.PurgeContent();
        turn.RecordUsage(null, null, 1.5m);
        turn.RecordUsage(null, null, 2.25m);
        turn.Fail(turn.Fence, AiAssistantErrors.Expired.Code, false, now.AddSeconds(2));
        Assert.Equal(3.75m, turn.AudioSeconds);
        Assert.Null(turn.ProtectedQuestion);
        Assert.Null(turn.ProtectedAnswer);
        Assert.True(turn.UsageUnknown);
    }
}
