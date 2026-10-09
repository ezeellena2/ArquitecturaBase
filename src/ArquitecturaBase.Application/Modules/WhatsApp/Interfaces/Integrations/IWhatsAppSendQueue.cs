
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>
/// Encola contenido cifrado en la transacción del llamador: ni el webhook ni los pedidos de la web esperan a Meta.
/// Los pendientes confirmados sobreviven al reinicio; un rollback también revierte el envío.
/// </summary>
public interface IWhatsAppSendQueue
{
    /// <summary>
    /// Devuelve <c>false</c> con capacidad agotada, contenido vencido o módulo apagado.
    /// Puede esperar a PostgreSQL, nunca a Meta. Aceptar no confirma la entrega.
    /// </summary>
    bool TryEnqueue(WhatsAppOutboundMessage message);
}
