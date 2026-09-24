namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Detalle que necesita la administración para mostrar y editar una cuenta.</summary>
public sealed record UserDetail(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles)
{
    public string? FormattedPhoneNumber { get; init; }

    public LastInvitation? LastInvitation { get; init; }
}
