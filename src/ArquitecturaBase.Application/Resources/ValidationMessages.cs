using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>Mensajes de Validation.resx en el idioma de la petición.</summary>
public static class ValidationMessages
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Validation", typeof(ValidationMessages).Assembly);

    public static string Required => Get(nameof(Required));
    public static string AiProviderInvalid => Get(nameof(AiProviderInvalid));
    public static string AiModelInvalid => Get(nameof(AiModelInvalid));
    public static string AiRevisionInvalid => Get(nameof(AiRevisionInvalid));
    public static string AiCredentialInvalid => Get(nameof(AiCredentialInvalid));

    public static string MaxLength => Get(nameof(MaxLength));
    public static string DisplayNameInvalid => Get(nameof(DisplayNameInvalid));

    public static string EmailInvalid => Get(nameof(EmailInvalid));

    public static string PageInvalid => Get(nameof(PageInvalid));

    public static string PageSizeInvalid => Get(nameof(PageSizeInvalid));

    public static string SortNotAllowed => Get(nameof(SortNotAllowed));

    public static string FormCatalogStateInvalid => Get(nameof(FormCatalogStateInvalid));

    public static string ReturnUrlInvalid => Get(nameof(ReturnUrlInvalid));

    public static string LoginCodeFormat => Get(nameof(LoginCodeFormat));

    public static string LoginCodeFormatWhatsApp => Get(nameof(LoginCodeFormatWhatsApp));

    /// <summary>El verify recibió el correo y el número: lleva uno de los dos.</summary>
    public static string EmailOrPhone => Get(nameof(EmailOrPhone));

    public static string CountryInvalid => Get(nameof(CountryInvalid));

    public static string RegistrationModeInvalid => Get(nameof(RegistrationModeInvalid));
    public static string SettingsRevisionInvalid => Get(nameof(SettingsRevisionInvalid));
    public static string SettingsRequired => Get(nameof(SettingsRequired));
    public static string SettingsPageSizeInvalid => Get(nameof(SettingsPageSizeInvalid));
    public static string SettingsColorInvalid => Get(nameof(SettingsColorInvalid));
    public static string SettingsNameInvalid => Get(nameof(SettingsNameInvalid));
    public static string SettingsLogoUrlInvalid => Get(nameof(SettingsLogoUrlInvalid));

    /// <summary>A whole number out of its range, with the range in the request's language ({From:N0} and {To:N0}).</summary>
    public static string NumberBetween => Get(nameof(NumberBetween));

    public static string PermissionUnknown => Get(nameof(PermissionUnknown));

    public static string CultureInvalid => Get(nameof(CultureInvalid));

    public static string TimeZoneInvalid => Get(nameof(TimeZoneInvalid));

    public static string CreatedWithinDaysInvalid => Get(nameof(CreatedWithinDaysInvalid));

    /// <summary>La invitación no dice por dónde mandarla, o dice un canal que no existe.</summary>
    public static string InvitationChannelInvalid => Get(nameof(InvitationChannelInvalid));

    /// <summary>Invitar por correo a una cuenta sin correo: el texto de la opción deshabilitada del tablero.</summary>
    public static string InvitationEmailRequired => Get(nameof(InvitationEmailRequired));

    /// <summary>Invitar por WhatsApp a una cuenta sin número.</summary>
    public static string InvitationPhoneRequired => Get(nameof(InvitationPhoneRequired));

    /// <summary>Invitar por WhatsApp con WhatsApp sin configurar: no hay por dónde mandar la plantilla.</summary>
    public static string InvitationWhatsAppUnavailable => Get(nameof(InvitationWhatsAppUnavailable));

    public static string AiPlaceSearchInvalid => Get(nameof(AiPlaceSearchInvalid));
    public static string AiUsageMonthInvalid => Get(nameof(AiUsageMonthInvalid));
    public static string AiModelTokenPriceInvalid => Get(nameof(AiModelTokenPriceInvalid));
    public static string AiModelAudioPriceInvalid => Get(nameof(AiModelAudioPriceInvalid));
    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
