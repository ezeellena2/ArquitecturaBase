namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>Siempre la misma respuesta, exista o no la cuenta: no revela nada (sección 5.3).</summary>
public sealed record RequestLoginCodeResponse(int ResendAfterSeconds);
