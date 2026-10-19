using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAssistantAudioPipelineTests
{
    [Fact]
    public async Task Pending_audio_is_scanned_even_after_access_and_interpretation_have_no_pending_source()
    {
        await using var host=new WhatsAppAssistantInboundFixture{HasAudioWork=true,HasWork=false,AudioOnlyContact=true};
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","audio-prepare","submit-audio","get-audio","audio-complete"},host.Events);
        Assert.NotNull(host.AudioCompleted);Assert.Equal("pending",host.AudioCompleted.Value.State);Assert.Null(host.Completed);
        Assert.NotEqual(host.Scopes[1],host.Scopes[2]);Assert.NotEqual(host.Scopes[2],host.Scopes[4]);
    }
    [Fact]
    public async Task Completed_normalization_is_confirmed_before_a_separate_interpretation_operation_is_prepared()
    {
        await using var host=new WhatsAppAssistantInboundFixture{HasAudioWork=true,HasWork=false,AudioReady=true,InterpretAfterAudio=true};
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","audio-prepare","submit-audio","get-audio","audio-complete","prepare","get","submit","get","complete"},host.Events);
        Assert.NotNull(host.AudioCompleted);Assert.NotNull(host.AudioCompleted.Value.NormalizedAudio);
        Assert.NotNull(host.Completed);
        Assert.NotEqual(host.Work.RequestId,host.AudioWork.RequestId);
    }
    [Fact]
    public async Task Rejected_normalization_is_completed_without_polling_or_creating_an_interpretation()
    {
        await using var host=new WhatsAppAssistantInboundFixture{HasAudioWork=true,HasWork=false,RejectAudioSubmit=true};
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","audio-prepare","submit-audio","audio-complete"},host.Events);
        Assert.NotNull(host.AudioCompleted);Assert.True(host.AudioCompleted.IsFailure);Assert.Null(host.Completed);
    }

    [Fact]
    public async Task Prepared_interpretation_runs_after_audio_without_another_text_submission_or_recovery_read()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasAudioWork = true, HasWork = true };
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "access", "prepare", "audio-prepare", "submit-audio", "get-audio", "audio-complete",
            "get", "submit", "get", "complete" }, host.Events);
        Assert.Equal(1, host.Events.Count(value => value == "prepare"));
        Assert.Equal(1, host.Events.Count(value => value == "submit"));
        Assert.Equal(2, host.Events.Count(value => value == "get"));
        Assert.NotNull(host.Completed);
        Assert.NotNull(host.AudioCompleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pending")]
    [InlineData("completed")]
    public async Task Exhausted_audio_quota_recovers_only_an_existing_acceptance_and_never_submits(string? state)
    {
        await using var host = new WhatsAppAssistantInboundFixture {
            HasAudioWork = true, HasWork = false, AudioRecoverOnly = true, ExistingAudioState = state };
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("submit-audio", host.Events);
        Assert.DoesNotContain("submit", host.Events);
        Assert.Equal(1, host.Events.Count(value => value == "get-audio"));
        Assert.NotNull(host.AudioCompleted);
        Assert.Null(host.Completed);
        if (state is null)
        {
            Assert.True(host.AudioCompleted.IsFailure);
            Assert.Equal(ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppAssistantErrors.StateConflict, host.AudioCompleted.Error);
        }
        else
        {
            Assert.True(host.AudioCompleted.IsSuccess);
            Assert.Equal(state, host.AudioCompleted.Value.State);
        }
    }
}
