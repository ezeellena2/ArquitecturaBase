using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

/// <summary>
/// El canal telefónico de WhatsApp. Prendido, ofrece los países de Configuración › WhatsApp (RC3-A, leídos en cada uso) y el
/// número del bot; apagado, nada. La regla del país de un número nuevo vale en los dos casos, como siempre: un número de un país
/// al que no se le mandan códigos responde <c>Auth.WhatsApp.CountryNotSupported</c>. Reemplaza al canal apagado del núcleo.
/// </summary>
internal sealed class WhatsAppPhoneChannel(
    IWhatsAppAvailability availability,
    IOptions<WhatsAppLoginOptions> options,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppSettingsReader settings) : IPhoneChannel
{
    public bool IsEnabled => availability.IsEnabled;

    public async Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken) =>
        availability.IsEnabled ? (await settings.FindAsync(cancellationToken)).AllowedCountries : [];

    public string? DisplayNumber => availability.IsEnabled ? options.Value.DisplayPhoneNumber : null;

    public async Task<Result> EnsureCanSendToAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        WhatsAppCountries.Allows((await settings.FindAsync(cancellationToken)).AllowedCountries, phoneNumbers.RegionOf(phone))
            ? Result.Success() : WhatsAppErrors.CountryNotSupported;
}
