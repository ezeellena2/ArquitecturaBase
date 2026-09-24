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
    IReadOnlyCollection<string> Roles)
{
    /// <summary>
    /// El número para mostrar ("+54 9 11 2345-6789"), como en /api/me: el front nunca muestra el E.164. Null sin número.
    /// Lo completa el caso de uso con el parser, que sabe agrupar cada país.
    /// </summary>
    public string? FormattedPhoneNumber { get; init; }

    /// <summary>La última invitación que se le mandó, o null si nunca se la invitó (sección 12 del spec del ingreso con WhatsApp).</summary>
    public LastInvitation? LastInvitation { get; init; }
}
