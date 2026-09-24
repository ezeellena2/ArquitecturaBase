using System.Globalization;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>Idioma de una cuenta: el de la petición si está soportado; si no, español.</summary>
internal static class UserCultures
{
    public const string Default = "es";

    private static readonly string[] Supported = [Default, "en"];

    public static bool IsSupported(string? culture) => culture is not null && Supported.Contains(culture, StringComparer.Ordinal);

    public static string FromCurrentRequest()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return IsSupported(language) ? language : Default;
    }

    /// <summary>
    /// El idioma en que se le escribe a la persona: el de su cuenta o, si todavía no tiene una, el de la petición. Son
    /// los mismos códigos que los de la plantilla de WhatsApp ("es" y "en").
    /// </summary>
    public static string Of(UserAccount? user) =>
        user is not null && IsSupported(user.Culture) ? user.Culture : FromCurrentRequest();
}
