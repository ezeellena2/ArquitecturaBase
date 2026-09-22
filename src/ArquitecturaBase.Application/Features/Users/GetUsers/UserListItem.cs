namespace ArquitecturaBase.Application.Features.Users.GetUsers;

/// <summary>
/// Una fila del listado. Trae los roles porque la tabla los muestra: sin ellos, para saber qué rol tiene
/// alguien habría que abrir su diálogo de roles fila por fila.
/// </summary>
public sealed record UserListItem(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
