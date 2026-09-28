using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Las reglas de una cuenta que no dependen de Identity: los topes de sus textos, el contacto obligatorio y el nombre
/// que llega de afuera. La cuenta (ApplicationUser) vive en Infrastructure; sus columnas y los validadores de
/// Application toman los topes de acá, así que hay un solo número.
/// </summary>
public static class AccountRules
{
    public const int DisplayNameMaxLength = 100;
    public const int CultureMaxLength = 10;
    public const int TimeZoneIdMaxLength = 64;

    /// <summary>
    /// Toda cuenta tiene un correo o un número. Una sin ninguno de los dos es un bug de quien la arma, no un error de
    /// negocio: los casos de uso ya lo garantizan antes de llegar acá.
    /// </summary>
    public static void EnsureHasContact(bool hasEmail, bool hasPhone)
    {
        if (!hasEmail && !hasPhone)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(hasEmail));
        }
    }

    /// <summary>Si el nombre entra en la columna. Sin nombre también vale: es opcional.</summary>
    public static bool IsValidDisplayName(string? displayName) =>
        displayName is null || displayName.Length <= DisplayNameMaxLength;

    /// <summary>
    /// El nombre que manda otro (Google, el perfil de WhatsApp), que nadie tipeó y no se puede rechazar: sin el
    /// carácter nulo ni mitades sueltas de un emoji, sin espacios en los bordes y recortado al tope sin partir un
    /// emoji. Null si no queda nada.
    /// </summary>
    public static string? FitExternalDisplayName(string? displayName) =>
        displayName is null
            ? null
            : StorableText.Clean(StorableText.Sanitize(displayName).Trim(), DisplayNameMaxLength);
}
