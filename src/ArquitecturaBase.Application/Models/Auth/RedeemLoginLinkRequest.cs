namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>Canjea un enlace del chat para abrir la sesión del navegador.</summary>
public sealed record RedeemLoginLinkRequest(string? Token);
