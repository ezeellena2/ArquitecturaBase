namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The countries of Configuración › WhatsApp (RC3-A): a code goes, and a number is accepted to sign in or link, only if the number's country
/// (not the one chosen in the form: "+598…" with Argentina chosen is still a number of Uruguay) is one of them. Null is an unknown country.
/// </summary>
internal static class WhatsAppCountries
{
    public static bool Allows(IReadOnlyList<string> countries, string? region) =>
        region is not null && countries.Contains(region, StringComparer.Ordinal);
}
