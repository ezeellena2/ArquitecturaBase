using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Canal de invitación solicitado durante el alta de una cuenta.</summary>
public sealed record InvitationRequest(UserInvitationChannel? Channel, bool Consent);
