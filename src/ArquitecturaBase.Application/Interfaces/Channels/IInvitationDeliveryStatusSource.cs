using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// El estado de entrega de las invitaciones de un canal que lo sigue (con WhatsApp, el que avisa Meta por el webhook).
/// Cero o uno por canal; el correo no tiene. Solo lee, sin límite.
/// </summary>
public interface IInvitationDeliveryStatusSource
{
    UserInvitationChannel Channel { get; }

    /// <summary>
    /// El estado de la invitación que salió con <paramref name="providerMessageId"/>; Pending si todavía no tiene id o el
    /// proveedor no avisó nada. Las fallidas antes de salir (SendFailed) no llegan acá: las resuelve quien llama.
    /// </summary>
    Task<InvitationDeliveryStatus> FindStatusAsync(string? providerMessageId, CancellationToken cancellationToken);
}
