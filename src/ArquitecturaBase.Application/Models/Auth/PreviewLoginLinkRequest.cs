namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>La vista previa no consume el enlace del chat.</summary>
public sealed record PreviewLoginLinkRequest(string? Token)
{
    /// <summary>El token da acceso a la cuenta y no debe aparecer en los logs.</summary>
    public override string ToString() => nameof(PreviewLoginLinkRequest);
}
