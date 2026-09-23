using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.GetLoginMethods;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class GetLoginMethodsQueryHandlerTests
{
    private static readonly WhatsAppLoginOptions Settings = new()
    {
        AllowedCountries = ["AR", "UY"],
        DisplayPhoneNumber = "15551632662",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_whatsapp_on_it_lists_the_countries_and_the_number_of_the_bot()
    {
        var result = await Handler(google: true, whatsApp: true).Handle(new GetLoginMethodsQuery(), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Google);
        Assert.True(result.Value.WhatsApp);
        Assert.Equal(["AR", "UY"], result.Value.WhatsAppCountries);
        Assert.Equal("15551632662", result.Value.WhatsAppNumber);
    }

    [Fact]
    public async Task With_whatsapp_off_it_offers_neither_countries_nor_a_number()
    {
        var result = await Handler(google: false, whatsApp: false).Handle(new GetLoginMethodsQuery(), Ct);

        Assert.False(result.Value.Google);
        Assert.False(result.Value.WhatsApp);
        Assert.Empty(result.Value.WhatsAppCountries);
        Assert.Null(result.Value.WhatsAppNumber);
    }

    private static GetLoginMethodsQueryHandler Handler(bool google, bool whatsApp) =>
        new(new FakeGoogleAvailability(google), new FakeWhatsAppAvailability(whatsApp), Options.Create(Settings));
}
