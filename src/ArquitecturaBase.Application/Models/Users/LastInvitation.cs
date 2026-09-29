using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Última invitación y su estado de entrega: <c>Failed</c> si no se pudo mandar, por cualquier canal; si salió, el que
/// da la fuente de su canal si tiene una (con WhatsApp, el que avisa Meta), y si no (el correo), null.
/// </summary>
public sealed record LastInvitation(UserInvitationChannel Channel, DateTime SentAtUtc, InvitationDeliveryStatus? DeliveryStatus)
{
    /// <summary>
    /// La traduce de lo que proyecta el lector y del estado que dio la fuente de su canal
    /// (<paramref name="trackedStatus"/>, null si el canal no tiene fuente). Fallida si no se pudo mandar, por cualquier
    /// canal y diga lo que diga la fuente: el admin tiene que verla para reenviarla. Es una función pura: no lee nada.
    /// </summary>
    public static LastInvitation From(UserInvitationRow row, InvitationDeliveryStatus? trackedStatus)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new LastInvitation(row.Channel, row.SentAtUtc, row.SendFailed ? InvitationDeliveryStatus.Failed : trackedStatus);
    }
}
