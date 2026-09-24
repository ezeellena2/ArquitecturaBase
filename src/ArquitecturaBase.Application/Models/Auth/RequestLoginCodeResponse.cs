namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>La misma respuesta exista o no la cuenta: no revela si el correo está registrado.</summary>
public sealed record RequestLoginCodeResponse(int ResendAfterSeconds);
