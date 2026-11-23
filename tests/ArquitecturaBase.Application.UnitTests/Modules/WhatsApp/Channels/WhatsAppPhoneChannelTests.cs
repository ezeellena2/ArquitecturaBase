using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// El canal telefónico de WhatsApp: prendido, ofrece los países de Configuración › WhatsApp (RC3-A) y el número del bot; apagado,
/// nada. La regla del país de un número nuevo vale en los dos casos, y los países se leen en cada uso.
/// </summary>
public sealed class WhatsAppPhoneChannelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly WhatsAppLoginOptions Settings = new() { DisplayPhoneNumber = "15551632662" };

    [Fact]
    public async Task With_whatsapp_on_it_offers_the_countries_and_the_number_of_the_bot()
    {
        var channel = Channel(whatsApp: true);

        Assert.True(channel.IsEnabled);
        Assert.Equal(["AR", "UY"], await channel.ListCountriesAsync(Ct));
        Assert.Equal("15551632662", channel.DisplayNumber);
    }

    [Fact]
    public async Task With_whatsapp_off_it_offers_neither_countries_nor_a_number()
    {
        var channel = Channel(whatsApp: false);

        Assert.False(channel.IsEnabled);
        Assert.Empty(await channel.ListCountriesAsync(Ct));
        Assert.Null(channel.DisplayNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_number_from_an_allowed_country_is_accepted_and_one_from_another_is_not(bool whatsApp)
    {
        var channel = Channel(whatsApp);

        Assert.True((await channel.EnsureCanSendToAsync(PhoneNumber.Create("+5493515550101").Value, Ct)).IsSuccess);
        Assert.True((await channel.EnsureCanSendToAsync(PhoneNumber.Create("+59899123456").Value, Ct)).IsSuccess);

        var brazilian = await channel.EnsureCanSendToAsync(PhoneNumber.Create("+5511912345678").Value, Ct);
        Assert.True(brazilian.IsFailure);
        Assert.Equal(WhatsAppErrors.CountryNotSupported, brazilian.Error);
    }

    [Fact]
    public async Task A_country_saved_in_the_settings_counts_from_the_next_use()
    {
        var settings = new FakeWhatsAppSettings();
        var channel = new WhatsAppPhoneChannel(new FakeWhatsAppAvailability(true), Options.Create(Settings), new FakePhoneNumberParser(), settings);
        var uruguayan = PhoneNumber.Create("+59899123456").Value;
        Assert.Equal(WhatsAppErrors.CountryNotSupported, (await channel.EnsureCanSendToAsync(uruguayan, Ct)).Error);

        settings.Saved = FakeWhatsAppSettings.With(allowedCountries: ["AR", "UY"]).Saved;

        Assert.True((await channel.EnsureCanSendToAsync(uruguayan, Ct)).IsSuccess);
        Assert.Equal(["AR", "UY"], await channel.ListCountriesAsync(Ct));
    }

    private static WhatsAppPhoneChannel Channel(bool whatsApp) =>
        new(new FakeWhatsAppAvailability(whatsApp), Options.Create(Settings), new FakePhoneNumberParser(),
            FakeWhatsAppSettings.With(allowedCountries: ["AR", "UY"]));
}
