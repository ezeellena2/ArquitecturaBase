namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Estado de entrega de una invitación, serializado por nombre. Por WhatsApp puede ser cualquiera; por correo (N5),
/// <see cref="Pending"/> mientras espera en la cola, <see cref="Sent"/> cuando el servidor la aceptó, <see cref="Failed"/> si
/// no salió y <see cref="Uncertain"/> si se cortó mientras salía.
/// </summary>
public enum InvitationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,

    /// <summary>H7 y N8: no se sabe si salió; el administrador revisa antes de mandar otra.</summary>
    Uncertain = 6,
}
