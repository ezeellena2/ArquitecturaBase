using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// Los medios de ingreso salen de Google y del canal telefónico (<c>IPhoneChannel</c>). Qué países y qué número ofrece
/// el canal de WhatsApp lo prueba WhatsAppPhoneChannelTests.
/// </summary>
public sealed class LoginMethodsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_the_phone_channel_on_it_lists_its_countries_and_its_number()
    {
        var phone = new FakePhoneChannel(isEnabled: true, countries: ["AR", "UY"], displayNumber: "15551632662");

        var result = await Service(google: true, phone).GetLoginMethodsAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Google);
        Assert.True(result.Value.WhatsApp);
        Assert.Equal(["AR", "UY"], result.Value.WhatsAppCountries);
        Assert.Equal("15551632662", result.Value.WhatsAppNumber);
    }

    [Fact]
    public async Task With_the_phone_channel_off_it_offers_neither_countries_nor_a_number()
    {
        // Aunque el canal traiga países, apagado no se ofrecen.
        var phone = new FakePhoneChannel(isEnabled: false, countries: ["AR"], displayNumber: "15551632662");

        var result = await Service(google: false, phone).GetLoginMethodsAsync(Ct);

        Assert.False(result.Value.Google);
        Assert.False(result.Value.WhatsApp);
        Assert.Empty(result.Value.WhatsAppCountries);
        Assert.Null(result.Value.WhatsAppNumber);
    }

    private static LoginMethodsService Service(bool google, FakePhoneChannel phone) => new(
        new FakeGoogleAvailability(google),
        phone,
        NullLogger<LoginMethodsService>.Instance);
}
