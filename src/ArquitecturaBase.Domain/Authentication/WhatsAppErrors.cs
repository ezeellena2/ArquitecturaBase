using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class WhatsAppErrors
{
    public const string CountryNotSupportedCode = "Auth.WhatsApp.CountryNotSupported";

    /// <summary>
    /// El número es de un país al que todavía no se mandan códigos (<c>WhatsApp:AllowedCountries</c>, sección 13 del
    /// spec del ingreso con WhatsApp). Se decide con el país del número, no con el que se eligió en el formulario:
    /// "+598…" con Argentina elegida sigue siendo un número de Uruguay. Cada código se paga, con una tarifa por país.
    /// </summary>
    public static readonly Error CountryNotSupported = Error.Validation(
        CountryNotSupportedCode, "Login codes are not sent to numbers from that country yet.");
}
