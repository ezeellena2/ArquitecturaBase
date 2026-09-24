using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Interpreta y muestra números de celular. Solo acepta celulares, porque un número de WhatsApp siempre lo es,
/// y en Argentina los guarda con el 9 (+549…), que es como llegan de WhatsApp.
/// </summary>
public interface IPhoneNumberParser
{
    /// <summary>
    /// El número que tipeó una persona, con o sin 0, 15 o 9, espacios y guiones. <paramref name="country"/> es el
    /// código ISO 3166-1 alfa-2 del país elegido (por ejemplo, "AR") y se usa cuando el número no empieza con "+".
    /// Si no es un celular válido, devuelve <c>Users.Phone.Invalid</c>.
    /// </summary>
    Result<PhoneNumber> Parse(string? country, string? number);

    /// <summary>
    /// El número tal como lo manda WhatsApp en <c>wa_id</c>: solo dígitos, sin el "+". Pasa por las mismas reglas
    /// que <see cref="Parse"/>, así que un <c>wa_id</c> argentino sin el 9 queda con el 9.
    /// </summary>
    Result<PhoneNumber> FromWhatsAppId(string? waId);

    /// <summary>El número para mostrar o registrar, con el medio tapado: "+54 9 11 •••• 6789".</summary>
    string Mask(PhoneNumber phone);

    /// <summary>El formato internacional legible: "+54 9 11 2345-6789".</summary>
    string FormatInternational(PhoneNumber phone);

    /// <summary>
    /// El código ISO 3166-1 alfa-2 del país del número, en mayúsculas ("AR"), o null si no es de ningún país que se
    /// conozca. Donde varios países comparten el código (+1), es el del número: +1 416… es "CA".
    /// </summary>
    string? RegionOf(PhoneNumber phone);
}
