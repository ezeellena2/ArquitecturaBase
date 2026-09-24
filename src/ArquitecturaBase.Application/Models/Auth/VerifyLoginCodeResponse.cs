namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>Authorize original que el SPA vuelve a abrir después de crear la cookie de sesión.</summary>
public sealed record VerifyLoginCodeResponse(string ReturnUrl);
