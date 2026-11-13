using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>External payloads already sent by Meta templates and historical reply buttons.</summary>
public static class WhatsAppAccessActions
{
    public const string WantToEnter = "WANT_TO_ENTER";
    public const string HaveAccount = "HAVE_ACCOUNT";
    public const string CreateAccount = "CREATE_ACCOUNT";
    public const string DidNotRequestCode = "DID_NOT_REQUEST_CODE";

    private const string WantToEnterInPrefix = WantToEnter + ":";

    /// <summary>
    /// The «Quiero entrar» payload of a new invitation, with its language (H18): the link then goes out in the language of the
    /// invitation without reading accounts. Buttons already sent, without it, keep the language of the installation.
    /// </summary>
    public static string WantToEnterIn(string culture) => SystemSettings.IsSupportedCulture(culture)
        ? WantToEnterInPrefix + culture
        : throw new ArgumentException("An invitation goes out in a supported language.", nameof(culture));

    /// <summary>The language a «Quiero entrar» payload carries; null for any other, and for the buttons already sent.</summary>
    public static string? CultureOf(string? id) =>
        id is not null && id.StartsWith(WantToEnterInPrefix, StringComparison.Ordinal)
            && id[WantToEnterInPrefix.Length..] is var culture && SystemSettings.IsSupportedCulture(culture)
            ? culture
            : null;

    public static bool IsKnown(string? id) => id is WantToEnter or HaveAccount or CreateAccount or DidNotRequestCode || CultureOf(id) is not null;
}
