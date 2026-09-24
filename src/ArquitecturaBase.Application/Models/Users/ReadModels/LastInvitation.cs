using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users.ReadModels;

/// <summary>
/// La última invitación de la cuenta: por dónde salió, cuándo y, por WhatsApp, su estado de entrega. Por correo no hay
/// estado que seguir: <see cref="DeliveryStatus"/> es null.
/// </summary>
public sealed record LastInvitation(UserInvitationChannel Channel, DateTime SentAtUtc, InvitationDeliveryStatus? DeliveryStatus);
