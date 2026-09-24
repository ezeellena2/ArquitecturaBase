namespace ArquitecturaBase.Application.Models.Auth;

public sealed record ExternalSignInRequest(string? ReturnUrl)
{
    // MVC registra los argumentos de acción; la URL puede contener parámetros OIDC.
    public override string ToString() => nameof(ExternalSignInRequest);
}
