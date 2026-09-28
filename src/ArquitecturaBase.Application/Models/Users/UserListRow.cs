namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Una fila del listado, tal como la proyecta el lector. Trae los roles porque la tabla los muestra: sin ellos, para
/// saber qué rol tiene alguien habría que abrir su diálogo de roles fila por fila. Sin correo, la tabla muestra el número.
/// El número para mostrar lo suma el caso de uso en <see cref="UserListItemResponse"/>: la consulta no lo puede armar en
/// la base.
/// </summary>
public sealed record UserListRow(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
