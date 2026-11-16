namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Actualiza nombre, roles y medios de ingreso aportados sin borrar los omitidos, si la cuenta sigue en
/// <see cref="ExpectedRevision"/>, la revisión que se leyó (N2).
/// </summary>
public sealed record UpdateUserRequest(
    Guid UserId,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    long ExpectedRevision,
    string? Email = null,
    PhoneNumberInput? Phone = null);
