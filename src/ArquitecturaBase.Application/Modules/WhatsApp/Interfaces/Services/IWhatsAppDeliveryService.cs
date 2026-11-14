using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Registra el resultado del envío en el historial y en la invitación asociada.</summary>
public interface IWhatsAppDeliveryService
{
    Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken);

    Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);

    /// <summary>H7: no se sabe si salió (Meta pudo haberlo recibido); una invitación queda sin confirmar, no fallida.</summary>
    Task<Result> RecordUncertainAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);
}
