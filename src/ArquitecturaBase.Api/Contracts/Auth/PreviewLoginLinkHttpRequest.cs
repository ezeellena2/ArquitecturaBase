namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>El cuerpo de POST /account/login-link/preview: la vista previa no consume el enlace del chat.</summary>
public sealed record PreviewLoginLinkHttpRequest(string? Token)
{
    // MVC registra los argumentos de la acción con ToString(): el token da acceso a la cuenta.
    public override string ToString() => nameof(PreviewLoginLinkHttpRequest);
}
