using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

internal sealed class WhatsAppAssistantOperationCompletionSignal(IWhatsAppInboundSignal inboundSignal) : IAssistantOperationCompletionSignal
{
    public void Notify(string channelId)
    {
        if (channelId is "whatsapp-assistant" or "whatsapp-manual") inboundSignal.Notify();
    }
}
