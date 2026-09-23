using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Application.Features.WhatsApp.ReceiveWebhook;

/// <summary>
/// Guarda lo que trajo un webhook de WhatsApp, ya con la firma validada y leído (sección 7 del spec del ingreso con
/// WhatsApp). No responde nada ni llama a Meta: los mensajes quedan pendientes para el bot.
/// </summary>
public sealed record ReceiveWhatsAppWebhookCommand(WhatsAppWebhookBatch Batch) : ICommand
{
    // Solo cuántos mensajes y estados trajo: el lote tampoco imprime su contenido.
    public sealed override string ToString() => $"{nameof(ReceiveWhatsAppWebhookCommand)} {Batch}";
}
