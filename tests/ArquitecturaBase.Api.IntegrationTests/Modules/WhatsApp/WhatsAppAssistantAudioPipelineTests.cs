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
        Assert.Equal(new[]{"access","prepare","audio-prepare","submit-audio","get-audio","audio-complete"},host.Events);
        Assert.NotNull(host.AudioCompleted);Assert.NotNull(host.AudioCompleted.Value.NormalizedAudio);Assert.Null(host.Completed);
        host.HasAudioWork = false; host.Events.Clear();
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","get","submit","get","complete"},host.Events);
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
}
