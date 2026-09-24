namespace ArquitecturaBase.Application.Models.Identity;

/// <summary>Lo que devolvió el proveedor externo (por ejemplo, Google) al volver del challenge.</summary>
public sealed record ExternalLogin(
    string Provider,
    string ProviderKey,
    string? Email,
    bool EmailVerified,
    string? DisplayName);
