using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Última invitación y su estado de entrega: por WhatsApp, el que avisa Meta; por correo, solo <c>Failed</c> si la cola
/// no la tomó, y si no, null.
/// </summary>
public sealed record LastInvitation(UserInvitationChannel Channel, DateTime SentAtUtc, InvitationDeliveryStatus? DeliveryStatus)
{
    /// <summary>La traduce de lo que proyecta el lector. Es una función pura: no lee nada.</summary>
    public static LastInvitation From(UserInvitationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new LastInvitation(row.Channel, row.SentAtUtc, DeliveryStatusOf(row));
    }

    /// <summary>
    /// Fallida si no se pudo mandar, por cualquier canal: el admin tiene que verla para reenviarla. Si salió, por correo
    /// no hay estado que seguir; por WhatsApp, pendiente mientras no tenga el id de Meta o Meta no haya avisado nada, y si
    /// no, el último estado de entrega registrado del mensaje saliente.
    /// </summary>
    private static InvitationDeliveryStatus? DeliveryStatusOf(UserInvitationRow row)
    {
        if (row.SendFailed)
        {
            return InvitationDeliveryStatus.Failed;
        }

        if (row.Channel is not UserInvitationChannel.WhatsApp)
        {
            return null;
        }

        if (!row.HasWaMessageId)
        {
            return InvitationDeliveryStatus.Pending;
        }

        return row.OutboundStatus switch
        {
            WhatsAppMessageStatus.Sent => InvitationDeliveryStatus.Sent,
            WhatsAppMessageStatus.Delivered => InvitationDeliveryStatus.Delivered,
            WhatsAppMessageStatus.Read => InvitationDeliveryStatus.Read,
            WhatsAppMessageStatus.Failed => InvitationDeliveryStatus.Failed,
            _ => InvitationDeliveryStatus.Pending,
        };
    }
}
