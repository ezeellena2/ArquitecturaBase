using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Assistant;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class DisabledAssistantOperationsTests
{
    [Fact]
    public async Task The_disabled_channel_reports_not_installed_and_never_accepts_or_recovers_an_operation()
    {
        var service = new DisabledAssistantOperations();
        var ct = TestContext.Current.CancellationToken;
        var context = new AssistantChannelContext("fictional-channel", Guid.NewGuid(), null, "es", "UTC", new([], []), [], []);
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("notInstalled", (await service.GetAvailabilityAsync(ct)).State);
        Assert.Equal(AssistantOperationErrors.Unavailable, (await service.SubmitAsync(new(Guid.NewGuid(), context, "Fictional input", now, now.AddHours(1)), ct)).Error);
        Assert.Equal(AssistantOperationErrors.Unavailable, (await service.GetAsync(context, Guid.NewGuid(), ct)).Error);
    }

    [Fact]
    public async Task Cancellation_remains_observable_when_the_module_is_not_installed()
    {
        var service = new DisabledAssistantOperations();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAvailabilityAsync(caller.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubmitAsync(null!, caller.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(null!, Guid.NewGuid(), caller.Token));
    }
}
