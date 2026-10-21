using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppOutgoingBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Committed_sends_drain_before_the_worker_waits_for_more_work()
    {
        await using var host = new WhatsAppAssistantWorkerFixture(background: true);
        var second = host.Operation with { Id = Guid.NewGuid(), ContactId = Guid.NewGuid(),
            To = PhoneNumber.Create("+5493515550102").Value, DispatchReservationId = Guid.NewGuid() };
        host.Reservations = new Queue<WhatsAppDispatchReservationResponse>([new(true, host.Operation), new(true, second)]);
        var started = host.Clock.GetUtcNow();

        await RunAsync(host, async () =>
        {
            await host.Clock.FirstIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Equal(2, host.Events.Count(value => value == "post"));
            Assert.Equal(2, host.Events.Count(value => value == "complete"));
            Assert.Equal(3, host.Events.Count(value => value == "reserve"));
            Assert.Equal(started, host.Clock.GetUtcNow());
            Assert.Equal(1, host.Clock.IdleDelays);
        });
    }

    [Fact]
    public async Task A_committed_failure_without_a_dispatch_does_not_delay_the_next_send()
    {
        await using var host = new WhatsAppAssistantWorkerFixture(background: true);
        host.Reservations = new Queue<WhatsAppDispatchReservationResponse>([new(true, null), new(true, host.Operation)]);

        await RunAsync(host, async () =>
        {
            await host.Clock.FirstIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Single(host.Events, value => value == "post");
            Assert.Single(host.Events, value => value == "complete");
            Assert.Equal(3, host.Events.Count(value => value == "reserve"));
        });
    }

    [Fact]
    public async Task No_progress_waits_two_virtual_seconds_instead_of_spinning_on_unavailable_rows()
    {
        await using var host = new WhatsAppAssistantWorkerFixture(background: true) { Reservations = new() };

        await RunAsync(host, async () =>
        {
            await host.Clock.FirstIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Single(host.Events, value => value == "reserve");
            host.Clock.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.Single(host.Events, value => value == "reserve");
            host.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await host.Clock.SecondIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Equal(2, host.Events.Count(value => value == "reserve"));
            Assert.DoesNotContain("post", host.Events);
        });
    }

    [Fact]
    public async Task A_reservation_exception_keeps_the_existing_idle_delay_before_retrying()
    {
        await using var host = new WhatsAppAssistantWorkerFixture(background: true) { ThrowNextReservation = true };

        await RunAsync(host, async () =>
        {
            await host.Clock.FirstIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Single(host.Events, value => value == "reserve");
            Assert.DoesNotContain("post", host.Events);
            host.Clock.Advance(TimeSpan.FromSeconds(2));
            await host.Clock.SecondIdleDelay.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Single(host.Events, value => value == "post");
            Assert.Single(host.Events, value => value == "complete");
        });
    }

    [Fact]
    public async Task A_confirmed_reservation_is_progress_even_when_a_later_guard_prevents_the_post()
    {
        await using var host = new WhatsAppAssistantWorkerFixture { AllowPost = false };

        Assert.True(await host.Worker.ProcessNextAsync(Ct));
        Assert.DoesNotContain("post", host.Events);
        Assert.DoesNotContain("complete", host.Events);
    }

    private static async Task RunAsync(WhatsAppAssistantWorkerFixture host, Func<Task> check)
    {
        await host.Worker.StartAsync(Ct);
        try { await check(); }
        finally { await host.Worker.StopAsync(Ct); }
    }
}
