using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class AssistantOperationCompletionSignalTests
{
    [Theory]
    [InlineData("home")]
    [InlineData("fixture-channel")]
    [InlineData("")]
    public void Core_without_an_optional_channel_keeps_completion_notifications_inert(string channelId)
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        var signal = provider.GetRequiredService<IAssistantOperationCompletionSignal>();
        Assert.IsType<DisabledAssistantOperationCompletionSignal>(signal);
        signal.Notify(channelId);
    }
}
