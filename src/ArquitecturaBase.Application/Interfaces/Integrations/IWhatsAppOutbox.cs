using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Encola un mensaje de WhatsApp para mandarlo en segundo plano: ni el webhook ni los pedidos de la web esperan a Meta
/// (sección 9 del spec). Si la app se reinicia con algo en la cola, se pierde, igual que un correo.
/// </summary>
public interface IWhatsAppOutbox
{
    /// <summary>
    /// Nunca espera. Devuelve <c>false</c> si el mensaje no entró (la cola está llena o WhatsApp está apagado): quien
    /// llama sabe así si el mensaje va a salir, por ejemplo para marcar el código como enviado solo si entró.
    /// </summary>
    bool TryEnqueue(WhatsAppOutboundMessage message);
}
