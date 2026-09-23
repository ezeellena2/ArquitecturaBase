namespace ArquitecturaBase.Application.Features.Users.GetUser;

/// <summary>
/// El usuario con sus roles: lo que necesita el diálogo de edición (sección 10 del spec de la Fase 4). El correo y
/// el número pueden faltar, y cada uno dice si la persona ya demostró que es suyo.
/// </summary>
public sealed record UserDetail(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
