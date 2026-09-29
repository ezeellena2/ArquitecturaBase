using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

/// <summary>
/// El canal telefónico de WhatsApp. Prendido, ofrece los países de <c>WhatsApp:AllowedCountries</c> y el número del bot;
/// apagado, nada. La regla del país de un número nuevo vale en los dos casos, como siempre: un número de un país al que no
/// se le mandan códigos responde <c>Auth.WhatsApp.CountryNotSupported</c>. Reemplaza al canal apagado del núcleo.
/// </summary>
internal sealed class WhatsAppPhoneChannel(
    IWhatsAppAvailability availability,
    IOptions<WhatsAppLoginOptions> options,
    IPhoneNumberParser phoneNumbers) : IPhoneChannel
{
    public bool IsEnabled => availability.IsEnabled;

    public IReadOnlyList<string> Countries => availability.IsEnabled ? options.Value.Countries : [];

    public string? DisplayNumber => availability.IsEnabled ? options.Value.DisplayPhoneNumber : null;

    public Result EnsureCanSendTo(PhoneNumber phone) =>
        options.Value.AllowsCountry(phoneNumbers.RegionOf(phone)) ? Result.Success() : WhatsAppErrors.CountryNotSupported;
}
