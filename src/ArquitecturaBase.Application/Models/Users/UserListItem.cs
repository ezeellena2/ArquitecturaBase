namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Fila del listado de administración, con roles y teléfono listo para mostrar.</summary>
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
    public string? FormattedPhoneNumber { get; init; }
}
