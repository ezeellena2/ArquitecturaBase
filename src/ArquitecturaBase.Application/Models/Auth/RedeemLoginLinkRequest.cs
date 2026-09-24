namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>Canjea un enlace del chat para abrir la sesión del navegador.</summary>
public sealed record RedeemLoginLinkRequest(string? Token)
{
    /// <summary>El token da acceso a la cuenta y no debe aparecer en los logs.</summary>
    public override string ToString() => nameof(RedeemLoginLinkRequest);
}
