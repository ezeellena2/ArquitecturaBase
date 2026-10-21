using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowReplyBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_persisted_reply_notice_runs_capture_without_advancing_the_poll_clock()
    {
        await using var host = new WhatsAppFlowReplyWorkerFixture();
        await host.Worker.StartAsync(Ct);
        await host.Clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var started = host.Clock.GetUtcNow();

        ((IWhatsAppFlowReplySignal)host.Signal).Notify();
        await host.SecondRound.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        Assert.Equal(started, host.Clock.GetUtcNow());
        Assert.Equal(2, host.Calls);
    }

    [Fact]
    public async Task Missing_notices_keep_the_existing_thirty_second_durable_fallback()
    {
        await using var host = new WhatsAppFlowReplyWorkerFixture();
        await host.Worker.StartAsync(Ct);
        await host.Clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        host.Clock.Advance(TimeSpan.FromMilliseconds(29999));
        Assert.Equal(1, host.Calls);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await host.SecondRound.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(2, host.Calls);
    }

    [Fact]
    public async Task Disabled_capture_does_not_start_on_a_notice()
    {
        await using var host = new WhatsAppFlowReplyWorkerFixture(enabled: false);
        ((IWhatsAppFlowReplySignal)host.Signal).Notify();
        await host.Worker.StartAsync(Ct);
        await host.Worker.StopAsync(Ct);
        Assert.Equal(0, host.Calls);
    }

    [Fact]
    public async Task Inbound_and_capture_notices_do_not_consume_each_others_wakeup()
    {
        var signal = new WhatsAppInboundSignal();
        var clock = new FakeTimeProvider();
        var inbound = signal.WaitAsync(TimeSpan.FromSeconds(30), clock, Ct);
        var capture = signal.WaitForFlowReplyAsync(TimeSpan.FromSeconds(30), clock, Ct);

        ((IWhatsAppFlowReplySignal)signal).Notify();
        await capture.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.False(inbound.IsCompleted);
        signal.Notify();
        await inbound.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var nextCapture = signal.WaitForFlowReplyAsync(TimeSpan.FromSeconds(30), clock, Ct);
        signal.Notify();
        await signal.WaitAsync(TimeSpan.FromSeconds(30), clock, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.False(nextCapture.IsCompleted);
        ((IWhatsAppFlowReplySignal)signal).Notify();
        await nextCapture.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task Several_capture_notices_coalesce_and_shutdown_cancels_the_wait()
    {
        var signal = new WhatsAppInboundSignal();
        var clock = new FakeTimeProvider();
        ((IWhatsAppFlowReplySignal)signal).Notify();
        ((IWhatsAppFlowReplySignal)signal).Notify();
        await signal.WaitForFlowReplyAsync(TimeSpan.FromSeconds(30), clock, Ct);
        using var stopping = new CancellationTokenSource();
        var next = signal.WaitForFlowReplyAsync(TimeSpan.FromSeconds(30), clock, stopping.Token);
        Assert.False(next.IsCompleted);
        await stopping.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }
}
