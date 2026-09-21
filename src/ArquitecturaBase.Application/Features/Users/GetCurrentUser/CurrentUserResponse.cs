namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

/// <summary>Perfil, roles, permisos, idioma/zona horaria y último ingreso: lo que el front necesita al iniciar (sección 5.6).</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTime? LastLoginAtUtc);
