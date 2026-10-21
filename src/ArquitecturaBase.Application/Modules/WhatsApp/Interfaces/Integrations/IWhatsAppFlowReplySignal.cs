namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>A coalescing notice after a Flow reply is committed; durable polling remains the fallback.</summary>
public interface IWhatsAppFlowReplySignal
{
    void Notify();
}
