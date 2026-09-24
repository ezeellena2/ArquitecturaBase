namespace ArquitecturaBase.Application.Models.Users.ReadModels;

/// <summary>
/// Una fila del listado. Trae los roles porque la tabla los muestra: sin ellos, para saber qué rol tiene
/// alguien habría que abrir su diálogo de roles fila por fila. Sin correo, la tabla muestra el número.
/// </summary>
public sealed record UserListItem(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles)
{
    /// <summary>
    /// El número para mostrar ("+54 9 11 2345-6789"), como en /api/me: el front nunca muestra el E.164. Null sin número.
    /// Lo completa el caso de uso con el parser: la consulta no lo puede armar en la base.
    /// </summary>
    public string? FormattedPhoneNumber { get; init; }
}
