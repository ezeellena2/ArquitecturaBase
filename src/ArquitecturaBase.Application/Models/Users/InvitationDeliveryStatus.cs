namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Estado de entrega de una invitación, serializado por nombre. Por WhatsApp puede ser cualquiera; por correo, solo
/// <see cref="Failed"/>, cuando la cola no la tomó.
/// </summary>
public enum InvitationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
}
