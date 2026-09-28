using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// La última invitación de una cuenta, tal como la proyecta el lector, con el estado del mensaje saliente que le
/// corresponde si salió por WhatsApp. <see cref="OutboundStatus"/> es null si no hay mensaje guardado con ese id o Meta
/// todavía no avisó ningún estado. El detalle la traduce con <see cref="LastInvitation.From"/>.
/// </summary>
public sealed record UserInvitationRow(
    UserInvitationChannel Channel,
    DateTime SentAtUtc,
    bool SendFailed,
    bool HasWaMessageId,
    WhatsAppMessageStatus? OutboundStatus);
