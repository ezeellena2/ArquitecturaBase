using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Application.Features.WhatsApp.RecordUnsentMessage;

/// <summary>
/// Un mensaje que la cola no pudo mandar: Meta lo rechazó al recibirlo (por ejemplo, la plantilla todavía no está
/// aprobada en ese idioma), el token no sirve o se agotaron los reintentos (sección 9 del spec del ingreso con WhatsApp).
/// No hay id de Meta ni nada que guardar en el historial; lo único que cambia es una invitación, que el admin tiene que
/// ver como fallida para reenviarla o invitar por correo. Lo manda la cola de salida.
/// </summary>
public sealed record RecordUnsentWhatsAppMessageCommand(WhatsAppOutboundMessage Message) : ICommand
{
    // El mensaje ya imprime solo su tipo.
    public override string ToString() => $"{nameof(RecordUnsentWhatsAppMessageCommand)} ({Message})";
}
