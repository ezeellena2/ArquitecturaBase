namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// El usuario con sus roles, tal como lo proyecta el lector: lo que necesita el diálogo de edición (sección 10 del spec
/// de la Fase 4). El correo y el número pueden faltar, y cada uno dice si la persona ya demostró que es suyo. El número
/// para mostrar y la última invitación los suma el caso de uso en <see cref="UserDetailResponse"/>.
/// </summary>
public sealed record UserDetailRow(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
