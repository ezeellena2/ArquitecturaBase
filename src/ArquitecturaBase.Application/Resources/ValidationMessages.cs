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

    public static string ReturnUrlInvalid => Get(nameof(ReturnUrlInvalid));

    public static string LoginCodeFormat => Get(nameof(LoginCodeFormat));

    public static string RegistrationModeInvalid => Get(nameof(RegistrationModeInvalid));

    public static string PermissionUnknown => Get(nameof(PermissionUnknown));

    public static string CultureInvalid => Get(nameof(CultureInvalid));

    public static string TimeZoneInvalid => Get(nameof(TimeZoneInvalid));

    public static string CreatedWithinDaysInvalid => Get(nameof(CreatedWithinDaysInvalid));

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
