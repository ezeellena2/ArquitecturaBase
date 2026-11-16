using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de POST /api/me/email/confirmation-code: qué cambio del número confirma el código que se manda al correo
/// verificado de la cuenta (<c>LinkPhone</c> o <c>UnlinkPhone</c>).
/// </summary>
public sealed record RequestEmailConfirmationCodeHttpRequest(ProfileChange? Change);
