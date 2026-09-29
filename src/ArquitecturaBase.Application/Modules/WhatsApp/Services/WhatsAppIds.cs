using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El número tal como lo manda WhatsApp en <c>wa_id</c>: solo dígitos, sin el "+". Pasa por las mismas reglas que un
/// número que tipea una persona (<see cref="IPhoneNumberParser.Parse"/> con el "+"), así que un <c>wa_id</c> argentino
/// sin el 9 queda con el 9.
/// </summary>
internal static class WhatsAppIds
{
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    public static Result<PhoneNumber> Parse(IPhoneNumberParser phoneNumbers, string? waId)
    {
        ArgumentNullException.ThrowIfNull(phoneNumbers);

        var digits = waId?.Trim();

        if (string.IsNullOrEmpty(digits)
            || digits.Length is < MinDigits or > MaxDigits
            || digits.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            return UserErrors.PhoneInvalid;
        }

        return phoneNumbers.Parse(country: null, "+" + digits);
    }
}
