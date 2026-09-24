using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Reenvía una invitación al contacto actual de una cuenta.</summary>
public sealed record SendUserInvitationRequest(Guid UserId, UserInvitationChannel? Channel, bool Consent);
