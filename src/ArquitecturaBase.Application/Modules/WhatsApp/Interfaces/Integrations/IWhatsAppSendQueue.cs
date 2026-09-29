
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>
/// Encola un mensaje de WhatsApp para mandarlo en segundo plano: ni el webhook ni los pedidos de la web esperan a Meta
/// (sección 9 del spec). La cola vive en memoria, como la del correo (backend.md, "Colas en memoria"): si la app se
/// reinicia con algo en la cola, se pierde, y un mensaje encolado antes de un commit que falla sale igual.
/// </summary>
public interface IWhatsAppSendQueue
{
    /// <summary>
    /// Nunca espera. Devuelve <c>false</c> si el mensaje no entró (la cola está llena o WhatsApp está apagado): quien
    /// llama sabe así si el mensaje va a salir, por ejemplo para marcar el código como enviado solo si entró.
    /// </summary>
    bool TryEnqueue(WhatsAppOutboundMessage message);
}
