using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// La última invitación de una cuenta, tal como la proyecta el lector, con el id que le dio el proveedor al mandarla
/// (<see cref="ProviderMessageId"/>, null si no salió o el canal no tiene proveedor que siga la entrega). El detalle le
/// pide el estado a la fuente de su canal y la traduce con <see cref="LastInvitation.From"/>.
/// </summary>
public sealed record UserInvitationRow(
    UserInvitationChannel Channel,
    DateTime SentAtUtc,
    bool SendFailed,
    string? ProviderMessageId);
