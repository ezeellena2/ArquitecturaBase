namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Estado de entrega de una invitación por WhatsApp, serializado por nombre.</summary>
public enum InvitationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
}
