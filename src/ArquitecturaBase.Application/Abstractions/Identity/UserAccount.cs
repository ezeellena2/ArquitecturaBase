namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>
/// Los datos del usuario que necesitan los casos de uso, sin exponer el modelo de Identity. El correo y el número
/// son opcionales, pero toda cuenta tiene al menos uno de los dos (sección 6.1 del spec del ingreso con WhatsApp).
/// El número va en formato internacional (+5491123456789).
/// </summary>
public sealed record UserAccount(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    bool IsActive);
