namespace ArquitecturaBase.Domain.Users;

/// <summary>Por dónde se manda una invitación (sección 6.6 del spec del ingreso con WhatsApp). Se guarda como texto.</summary>
public enum UserInvitationChannel
{
    Email = 1,

    WhatsApp = 2,
}
