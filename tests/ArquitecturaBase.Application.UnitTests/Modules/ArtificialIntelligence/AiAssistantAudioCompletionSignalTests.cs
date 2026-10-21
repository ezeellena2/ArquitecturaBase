using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioCompletionSignalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_normalized_audio_result_wakes_its_owner_after_the_protected_result_is_committed()
    {
        var fixture = new AssistantAudioExecutionFixture();
        Assert.True((await fixture.Dispatch.BeginProviderStepAsync(fixture.Claim, Ct)).Value);
        Assert.Empty(fixture.CompletionSignal.Channels);
        var completion = new AiAssistantAdvanceResponse(await fixture.ContextAsync(Ct), null, [], new(null, null) { AudioSeconds = 2.75m })
            { NormalizedAudio = new("Fictitious visit", ["en"]) };
        fixture.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(fixture.Inner.Work.InTransaction);
            Assert.Equal(2, fixture.Inner.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Completed, fixture.Turn.State);
            Assert.NotNull(fixture.Turn.ProtectedAnswer);
            Assert.Equal(2.75m, fixture.Turn.AudioSeconds);
        };
        Assert.True((await fixture.Dispatch.CompleteAsync(fixture.Claim, completion, Ct)).IsSuccess);
        Assert.Equal(fixture.Profile.ChannelContext.ChannelId, Assert.Single(fixture.CompletionSignal.Channels));
    }

    [Fact]
    public async Task Normalized_audio_is_not_announced_when_its_commit_fails()
    {
        var fixture = new AssistantAudioExecutionFixture();
        Assert.True((await fixture.Dispatch.BeginProviderStepAsync(fixture.Claim, Ct)).Value);
        var completion = new AiAssistantAdvanceResponse(await fixture.ContextAsync(Ct), null, [], null)
            { NormalizedAudio = new("Fictitious visit", ["en"]) };
        fixture.Inner.Work.CommitFailure = new IOException("Fictitious audio commit failure.");
        await Assert.ThrowsAsync<IOException>(() => fixture.Dispatch.CompleteAsync(fixture.Claim, completion, Ct));
        Assert.Empty(fixture.CompletionSignal.Channels);
    }
}
