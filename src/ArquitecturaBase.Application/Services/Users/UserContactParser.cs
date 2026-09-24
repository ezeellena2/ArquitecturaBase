using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Lee el correo y el número que carga un administrador en el alta o en la edición (sección 12 del spec del ingreso con
/// WhatsApp). Vacío es lo mismo que no haberlo mandado. Un número nuevo pasa por las mismas reglas que en el ingreso:
/// tiene que ser un celular (<c>Users.Phone.Invalid</c>) de un país habilitado (<c>Auth.WhatsApp.CountryNotSupported</c>),
/// porque es con el que la persona va a entrar. La regla del país es solo para uno nuevo: una cuenta puede tener un
/// número de otro país (el bot crea cuentas con el número del chat, y achicar <c>WhatsApp:AllowedCountries</c> deja
/// afuera números que ya estaban), y la edición que lo manda de vuelta no lo cambia.
/// </summary>
internal sealed class UserContactParser(IPhoneNumberParser phoneNumbers, IOptions<WhatsAppLoginOptions> whatsAppOptions)
{
    public static Result<Email?> ReadEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Success<Email?>(null);
        }

        var parsed = Email.Create(email);

        return parsed.IsSuccess ? Result.Success<Email?>(parsed.Value) : Result.Failure<Email?>(parsed.Error);
    }

    /// <summary>El número de un alta, que siempre es nuevo: tiene que ser un celular de un país habilitado.</summary>
    public Result<PhoneNumber?> ReadPhone(PhoneNumberInput? phone)
    {
        var parsed = ParsePhone(phone);

        if (parsed.IsFailure || parsed.Value is null)
        {
            return parsed;
        }

        var allowed = EnsureCountryAllowed(parsed.Value);

        return allowed.IsSuccess ? parsed : Result.Failure<PhoneNumber?>(allowed.Error);
    }

    /// <summary>
    /// Solo la forma: un celular, de cualquier país. La edición lo usa antes de saber si el número es nuevo, y controla el
    /// país (<see cref="EnsureCountryAllowed"/>) recién si lo es.
    /// </summary>
    public Result<PhoneNumber?> ParsePhone(PhoneNumberInput? phone)
    {
        if (phone is null || phone.IsEmpty)
        {
            return Result.Success<PhoneNumber?>(null);
        }

        var parsed = phoneNumbers.Parse(phone.Country, phone.Number);

        return parsed.IsSuccess ? Result.Success<PhoneNumber?>(parsed.Value) : Result.Failure<PhoneNumber?>(parsed.Error);
    }

    /// <summary>Si se mandan códigos a números del país de <paramref name="phone"/>, como en el ingreso.</summary>
    public Result EnsureCountryAllowed(PhoneNumber phone) =>
        whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone))
            ? Result.Success()
            : WhatsAppErrors.CountryNotSupported;
}
