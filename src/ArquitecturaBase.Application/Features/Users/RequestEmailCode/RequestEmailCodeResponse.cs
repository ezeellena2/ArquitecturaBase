namespace ArquitecturaBase.Application.Features.Users.RequestEmailCode;

/// <summary>Siempre la misma respuesta, sea el correo libre o de otra cuenta: no revela nada.</summary>
public sealed record RequestEmailCodeResponse(int ResendAfterSeconds);
