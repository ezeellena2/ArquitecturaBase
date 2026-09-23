using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// El outbox con WhatsApp apagado, para que Application nunca reciba un null. No debería llegarle nada, porque los casos
/// de uso miran IWhatsAppAvailability antes; si algo llega, no lo encola y lo registra.
/// </summary>
internal sealed partial class DisabledWhatsAppOutbox(ILogger<DisabledWhatsAppOutbox> logger) : IWhatsAppOutbox
{
    public bool TryEnqueue(WhatsAppOutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogDropped(logger, message.GetType().Name);

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp is disabled (no WhatsApp:PhoneNumberId); a {MessageType} was not queued")]
    private static partial void LogDropped(ILogger logger, string messageType);
}
