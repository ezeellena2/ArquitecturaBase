namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Perfil, roles, permisos, idioma/zona horaria y último ingreso: lo que el front necesita al iniciar (sección 5.6).
/// El correo y el número pueden faltar, y cada uno dice si está verificado. Con <see cref="HasGoogleLogin"/>, el
/// perfil sabe con qué medios puede entrar la persona (sección 12 del spec del ingreso con WhatsApp).
/// El número viaja tres veces: <see cref="PhoneNumber"/> en E.164, para comparar; <see cref="FormattedPhoneNumber"/>
/// ("+54 9 11 2345-6789"), para mostrar; y <see cref="MaskedPhoneNumber"/> ("+54 9 11 •••• 6789"), para confirmar
/// algo sin repetir el número entero. Los arma el parser, que sabe agrupar cada país: el front nunca muestra el E.164.
/// </summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    string? FormattedPhoneNumber,
    string? MaskedPhoneNumber,
    bool PhoneNumberConfirmed,
    bool HasGoogleLogin,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTime? LastLoginAtUtc);
