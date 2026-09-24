namespace ArquitecturaBase.Application.Models.Users;

/// <summary>La misma respuesta para cualquier correo, esté libre u ocupado.</summary>
public sealed record RequestEmailCodeResponse(int ResendAfterSeconds);
