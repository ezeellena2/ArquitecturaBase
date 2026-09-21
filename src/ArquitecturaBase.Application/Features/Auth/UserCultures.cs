using System.Globalization;

namespace ArquitecturaBase.Application.Features.Auth;

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
}
