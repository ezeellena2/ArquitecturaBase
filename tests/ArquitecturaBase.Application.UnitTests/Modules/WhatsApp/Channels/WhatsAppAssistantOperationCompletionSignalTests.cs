using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

public sealed class WhatsAppAssistantOperationCompletionSignalTests
{
    [Theory]
    [InlineData("whatsapp-assistant", 1)]
    [InlineData("whatsapp-manual", 1)]
    [InlineData("home", 0)]
    [InlineData("fixture-channel", 0)]
    [InlineData("whatsapp-assistant-other", 0)]
    [InlineData("WHATSAPP-ASSISTANT", 0)]
    [InlineData("", 0)]
    public void Only_the_channels_owned_by_WhatsApp_wake_its_inbound_processor(string channelId, int expected)
    {
        var inbound = new RecordingWhatsAppInboundSignal();
        var signal = new WhatsAppAssistantOperationCompletionSignal(inbound);
        signal.Notify(channelId);
        Assert.Equal(expected, inbound.Notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_module_replaces_the_disabled_signal_once_in_either_registration_order(bool moduleFirst)
    {
        var inbound = new RecordingWhatsAppInboundSignal();
        var services = new ServiceCollection().AddSingleton<IWhatsAppInboundSignal>(inbound);
        if (moduleFirst) services.AddWhatsAppApplication().AddApplication();
        else services.AddApplication().AddWhatsAppApplication();
        services.AddWhatsAppApplication();
        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IAssistantOperationCompletionSignal));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(WhatsAppAssistantOperationCompletionSignal), descriptor.ImplementationType);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAssistantOperationCompletionSignal>().Notify("whatsapp-assistant");
        Assert.Equal(1, inbound.Notifications);
    }
}
