namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Detalle que necesita la administración para mostrar y editar una cuenta.</summary>
public sealed record UserDetailResponse(
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
    /// <summary>
    /// El número para mostrar ("+54 9 11 2345-6789"), como en /api/me: el front nunca muestra el E.164. Null sin número.
    /// Lo completa el caso de uso con el parser, que sabe agrupar cada país.
    /// </summary>
    public string? FormattedPhoneNumber { get; init; }

    public LastInvitation? LastInvitation { get; init; }
}
