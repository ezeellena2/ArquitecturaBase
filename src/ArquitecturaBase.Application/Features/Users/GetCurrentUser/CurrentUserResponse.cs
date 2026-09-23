namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

/// <summary>
/// Perfil, roles, permisos, idioma/zona horaria y último ingreso: lo que el front necesita al iniciar (sección 5.6).
/// El correo y el número pueden faltar, y cada uno dice si está verificado. Con <see cref="HasGoogleLogin"/>, el
/// perfil sabe con qué medios puede entrar la persona (sección 12 del spec del ingreso con WhatsApp).
/// </summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool HasGoogleLogin,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTime? LastLoginAtUtc);
