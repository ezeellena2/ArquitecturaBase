using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>Canal de la invitación opcional que acompaña el alta.</summary>
public sealed record InvitationHttpRequest(UserInvitationChannel? Channel, bool Consent);
