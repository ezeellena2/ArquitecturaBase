namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>Query del navegador; evita que MVC registre parámetros OIDC en el valor del argumento.</summary>
public sealed class ExternalLoginQuery
{
    public string? ReturnUrl { get; set; }

    public override string ToString() => nameof(ExternalLoginQuery);
}
