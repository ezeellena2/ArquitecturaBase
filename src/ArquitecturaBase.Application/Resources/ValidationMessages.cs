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

    public static string LoginCodeFormatWhatsApp => Get(nameof(LoginCodeFormatWhatsApp));

    /// <summary>El verify recibió el correo y el número: lleva uno de los dos.</summary>
    public static string EmailOrPhone => Get(nameof(EmailOrPhone));

    public static string CountryInvalid => Get(nameof(CountryInvalid));

    /// <summary>El token del enlace no tiene la forma de uno: casi siempre, un enlace copiado a medias.</summary>
    public static string LoginLinkTokenFormat => Get(nameof(LoginLinkTokenFormat));

    public static string RegistrationModeInvalid => Get(nameof(RegistrationModeInvalid));

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

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
