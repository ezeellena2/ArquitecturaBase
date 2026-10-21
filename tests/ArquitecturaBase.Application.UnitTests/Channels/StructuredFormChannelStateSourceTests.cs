using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class StructuredFormChannelStateSourceTests
{
    [Fact]
    public async Task The_disabled_channel_state_source_has_no_open_sends()
    {
        var source = new DisabledStructuredFormChannelStateSource();
        var result = await source.ListAsync([Guid.NewGuid(), Guid.NewGuid()], AtUtc, TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }
    [Fact]
    public async Task The_disabled_channel_state_source_honors_cancellation()
    {
        var source = new DisabledStructuredFormChannelStateSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync([], AtUtc, new CancellationToken(true)));
    }
    [Fact]
    public void Core_registration_alone_provides_the_disabled_state_source()
    {
        var services = new ServiceCollection(); services.AddApplication();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DisabledStructuredFormChannelStateSource>(provider.GetRequiredService<IStructuredFormChannelStateSource>());
    }
    private static DateTime AtUtc => new(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
}
