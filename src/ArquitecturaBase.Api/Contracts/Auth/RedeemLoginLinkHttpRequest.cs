namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>El cuerpo de POST /account/login-link/redeem: canjea el enlace del chat y abre la sesión.</summary>
public sealed record RedeemLoginLinkHttpRequest(string? Token)
{
    // MVC registra los argumentos de la acción con ToString(): el token da acceso a la cuenta.
    public override string ToString() => nameof(RedeemLoginLinkHttpRequest);
}
