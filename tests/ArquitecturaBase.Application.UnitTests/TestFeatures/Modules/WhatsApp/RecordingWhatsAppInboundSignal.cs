using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class RecordingWhatsAppInboundSignal : IWhatsAppInboundSignal
{
    public int Notifications { get; private set; }
    public void Notify() => Notifications++;
}
