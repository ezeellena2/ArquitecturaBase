using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Features.WhatsApp.RecordOutboundMessage;

/// <summary>
/// Guarda en el historial un mensaje que Meta aceptó mandar, con el id que devolvió (sección 9 del spec del ingreso con
/// WhatsApp): así los estados del webhook (enviado, entregado, leído, falló) encuentran su mensaje. Lo manda la cola de
/// salida después de cada envío correcto. Guarda el resumen seguro, nunca el código ni la URL del enlace.
/// </summary>
public sealed record RecordOutboundWhatsAppMessageCommand(WhatsAppOutboundMessage Message, string WaMessageId) : ICommand
{
    // El mensaje ya imprime solo su tipo; el id de Meta no dice nada de la persona, pero tampoco hace falta en un log.
    public override string ToString() => $"{nameof(RecordOutboundWhatsAppMessageCommand)} ({Message})";
}
