namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Los datos del usuario que necesitan los casos de uso, sin exponer el modelo de Identity.</summary>
public sealed record UserAccount(
    Guid Id,
    string Email,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    bool IsActive);
