using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// El canal telefónico de WhatsApp: prendido, ofrece los países y el número del bot; apagado, nada. La regla del país de
/// un número nuevo vale en los dos casos.
/// </summary>
public sealed class WhatsAppPhoneChannelTests
{
    private static readonly WhatsAppLoginOptions Settings = new()
    {
        AllowedCountries = ["AR", "UY"],
        DisplayPhoneNumber = "15551632662",
    };

    [Fact]
    public void With_whatsapp_on_it_offers_the_countries_and_the_number_of_the_bot()
    {
        var channel = Channel(whatsApp: true);

        Assert.True(channel.IsEnabled);
        Assert.Equal(["AR", "UY"], channel.Countries);
        Assert.Equal("15551632662", channel.DisplayNumber);
    }

    [Fact]
    public void With_whatsapp_off_it_offers_neither_countries_nor_a_number()
    {
        var channel = Channel(whatsApp: false);

        Assert.False(channel.IsEnabled);
        Assert.Empty(channel.Countries);
        Assert.Null(channel.DisplayNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_number_from_an_allowed_country_is_accepted_and_one_from_another_is_not(bool whatsApp)
    {
        var channel = Channel(whatsApp);

        Assert.True(channel.EnsureCanSendTo(PhoneNumber.Create("+5493515550101").Value).IsSuccess);
        Assert.True(channel.EnsureCanSendTo(PhoneNumber.Create("+59899123456").Value).IsSuccess);

        var brazilian = channel.EnsureCanSendTo(PhoneNumber.Create("+5511912345678").Value);
        Assert.True(brazilian.IsFailure);
        Assert.Equal(WhatsAppErrors.CountryNotSupported, brazilian.Error);
    }

    private static WhatsAppPhoneChannel Channel(bool whatsApp) =>
        new(new FakeWhatsAppAvailability(whatsApp), Options.Create(Settings), new FakePhoneNumberParser());
}
