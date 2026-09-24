using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>Canal y consentimiento para reenviar la invitación a una cuenta.</summary>
public sealed record SendInvitationHttpRequest(UserInvitationChannel? Channel, bool Consent)
{
    public override string ToString() => nameof(SendInvitationHttpRequest);
}
