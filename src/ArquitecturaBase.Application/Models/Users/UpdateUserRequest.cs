namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Actualiza nombre, roles y medios de ingreso aportados sin borrar los omitidos.</summary>
public sealed record UpdateUserRequest(
    Guid UserId,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    string? Email = null,
    PhoneNumberInput? Phone = null);
