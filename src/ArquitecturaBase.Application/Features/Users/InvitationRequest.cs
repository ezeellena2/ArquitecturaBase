using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// La invitación que el alta le manda a la persona (sección 6.6 del spec del ingreso con WhatsApp): por dónde y, para
/// WhatsApp, la confirmación de que la persona aceptó recibir mensajes.
/// </summary>
public sealed record InvitationRequest(UserInvitationChannel? Channel, bool Consent);
