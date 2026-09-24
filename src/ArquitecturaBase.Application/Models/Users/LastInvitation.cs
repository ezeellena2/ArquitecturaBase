using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Última invitación enviada y, si fue por WhatsApp, su estado de entrega.</summary>
public sealed record LastInvitation(UserInvitationChannel Channel, DateTime SentAtUtc, InvitationDeliveryStatus? DeliveryStatus);
