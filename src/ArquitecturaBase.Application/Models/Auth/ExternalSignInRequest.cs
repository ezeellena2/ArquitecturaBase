namespace ArquitecturaBase.Application.Models.Auth;

public sealed record ExternalSignInRequest(string? ReturnUrl, bool? Register = null);
