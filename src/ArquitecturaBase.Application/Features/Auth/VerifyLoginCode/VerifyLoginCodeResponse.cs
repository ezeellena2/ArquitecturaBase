namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

/// <summary>A dónde navega el SPA: el authorize original, que ahora encuentra la sesión (sección 5.2).</summary>
public sealed record VerifyLoginCodeResponse(string ReturnUrl);
