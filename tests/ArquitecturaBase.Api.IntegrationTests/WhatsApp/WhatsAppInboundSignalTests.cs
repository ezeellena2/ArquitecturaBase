using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// El aviso entre el webhook y el procesador (sección 7 del spec): despierta al procesador sin esperar a la revisión
/// periódica, y muchos avisos seguidos lo despiertan una sola vez. Son de unidad, sin base.
/// </summary>
public sealed class WhatsAppInboundSignalTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notice_wakes_the_processor_before_the_poll_interval()
    {
        var signal = new WhatsAppInboundSignal();
        var clock = new FakeTimeProvider();
        var waiting = signal.WaitAsync(PollInterval, clock, Ct);

        signal.Notify();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Without_notices_it_waits_until_the_poll_interval()
    {
        var signal = new WhatsAppInboundSignal();
        var clock = new FakeTimeProvider();
        var waiting = signal.WaitAsync(PollInterval, clock, Ct);

        clock.Advance(PollInterval - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50, Ct);
        Assert.False(waiting.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    /// <summary>Mientras el procesador trabaja llegan varios avisos: alcanza con una vuelta más, que revisa la tabla entera.</summary>
    [Fact]
    public async Task Several_notices_while_it_is_busy_wake_it_once()
    {
        var signal = new WhatsAppInboundSignal();
        var clock = new FakeTimeProvider();

        signal.Notify();
        signal.Notify();
        signal.Notify();

        await signal.WaitAsync(PollInterval, clock, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = signal.WaitAsync(PollInterval, clock, Ct);
        await Task.Delay(50, Ct);

        Assert.False(second.IsCompleted);
        signal.Notify();
        await second.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_wait()
    {
        var signal = new WhatsAppInboundSignal();
        using var stopping = new CancellationTokenSource();
        var waiting = signal.WaitAsync(PollInterval, new FakeTimeProvider(), stopping.Token);

        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
