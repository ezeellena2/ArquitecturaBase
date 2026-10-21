using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppInboundPollingTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public async Task Live_assistant_or_audio_references_are_polled_every_two_seconds(bool assistant,bool audio)
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true)
            {HasWork=false,AudioOnlyContact=true,HasPendingAssistant=assistant,HasPendingAudio=audio};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        Assert.Equal(1,host.Rounds);

        host.Clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(1,host.Rounds);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        Assert.Equal(2,host.Rounds);
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task A_pending_operation_is_recovered_after_two_seconds_without_submitting_it_again()
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true){HasPendingAssistant=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        Assert.NotNull(host.Completed);
        Assert.Equal("pending",host.Completed.Value.State);
        Assert.Equal(1,host.Events.Count(value=>value=="submit"));

        host.ExistingAssistantState="completed";
        host.Clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal("pending",host.Completed.Value.State);
        Assert.Equal(1,host.Events.Count(value=>value=="complete"));
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(30),await host.WaitForPollAsync(Ct));
        Assert.Equal("completed",host.Completed.Value.State);
        Assert.Equal(host.Work.RequestId,host.Completed.Value.RequestId);
        Assert.Equal(2,host.Events.Count(value=>value=="complete"));
        Assert.Equal(1,host.Events.Count(value=>value=="submit"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Pending_references_preserve_a_configured_interval_that_is_already_as_fast(int seconds)
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true,inboundPollSeconds:seconds)
            {HasWork=false,AudioOnlyContact=true,HasPendingAssistant=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(seconds),await host.WaitForPollAsync(Ct));
        host.Clock.Advance(TimeSpan.FromSeconds(seconds)-TimeSpan.FromMilliseconds(1));
        Assert.Equal(1,host.Rounds);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(seconds),await host.WaitForPollAsync(Ct));
        Assert.Equal(2,host.Rounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public async Task Without_pending_references_the_processor_keeps_the_configured_interval(int seconds)
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true,inboundPollSeconds:seconds)
            {HasWork=false,AudioOnlyContact=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(seconds),await host.WaitForPollAsync(Ct));
        host.Clock.Advance(TimeSpan.FromSeconds(seconds)-TimeSpan.FromMilliseconds(1));
        Assert.Equal(1,host.Rounds);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(seconds),await host.WaitForPollAsync(Ct));
        Assert.Equal(2,host.Rounds);
    }

    [Fact]
    public async Task Completing_the_last_pending_reference_restores_the_configured_interval()
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true)
            {HasWork=false,AudioOnlyContact=true,HasPendingAssistant=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        host.HasPendingAssistant=false;
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(30),await host.WaitForPollAsync(Ct));
        Assert.Equal(2,host.Rounds);
    }

    [Fact]
    public async Task Audio_waiting_for_its_retry_keeps_fast_polling_without_starting_the_effect_early()
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true)
            {HasWork=false,AudioOnlyContact=true,HasAudioWork=true,HasPendingAudio=true};
        host.AudioRetryDueAtUtc=host.Clock.GetUtcNow().UtcDateTime.AddSeconds(6);
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        Assert.Empty(host.Events);
        for(var round=0;round<2;round++)
        {
            host.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
            Assert.Empty(host.Events);
        }
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2),await host.WaitForPollAsync(Ct));
        Assert.Equal(1,host.Events.Count(value=>value=="submit-audio"));
        Assert.NotNull(host.AudioCompleted);
    }

    [Fact]
    public async Task A_notice_still_wakes_an_idle_processor_without_advancing_the_clock()
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true)
            {HasWork=false,AudioOnlyContact=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(30),await host.WaitForPollAsync(Ct));
        var now=host.Clock.GetUtcNow();
        host.Signal.Notify();
        Assert.Equal(TimeSpan.FromSeconds(30),await host.WaitForPollAsync(Ct));
        Assert.Equal(now,host.Clock.GetUtcNow());
        Assert.Equal(2,host.Rounds);
    }

    [Fact]
    public async Task Stopping_the_processor_finishes_its_wait_without_advancing_the_clock()
    {
        await using var host=new WhatsAppAssistantInboundFixture(processInBackground:true)
            {HasWork=false,AudioOnlyContact=true};
        await host.Processor.StartAsync(Ct);
        Assert.Equal(TimeSpan.FromSeconds(30),await host.WaitForPollAsync(Ct));
        await host.Processor.StopAsync(Ct);
        Assert.NotNull(host.Processor.ExecuteTask);
        Assert.True(host.Processor.ExecuteTask.IsCompleted);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1,host.Rounds);
    }
}