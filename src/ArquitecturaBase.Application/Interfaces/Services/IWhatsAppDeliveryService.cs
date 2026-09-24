using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Registra el resultado del envío en el historial y en la invitación asociada.</summary>
public interface IWhatsAppDeliveryService
{
    Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken);

    Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);
}
