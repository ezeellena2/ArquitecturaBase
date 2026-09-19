using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>Mensajes de Validation.resx en el idioma de la petición.</summary>
public static class ValidationMessages
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Validation", typeof(ValidationMessages).Assembly);

    public static string Required => Get(nameof(Required));

    public static string MaxLength => Get(nameof(MaxLength));

    public static string EmailInvalid => Get(nameof(EmailInvalid));

    public static string PageInvalid => Get(nameof(PageInvalid));

    public static string PageSizeInvalid => Get(nameof(PageSizeInvalid));

    public static string SortNotAllowed => Get(nameof(SortNotAllowed));

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
