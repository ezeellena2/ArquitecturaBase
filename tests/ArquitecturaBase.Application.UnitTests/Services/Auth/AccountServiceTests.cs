using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class AccountServiceTests
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
        var result = await Service(google: true, whatsApp: true).GetLoginMethodsAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Google);
        Assert.True(result.Value.WhatsApp);
        Assert.Equal(["AR", "UY"], result.Value.WhatsAppCountries);
        Assert.Equal("15551632662", result.Value.WhatsAppNumber);
    }

    [Fact]
    public async Task With_whatsapp_off_it_offers_neither_countries_nor_a_number()
    {
        var result = await Service(google: false, whatsApp: false).GetLoginMethodsAsync(Ct);

        Assert.False(result.Value.Google);
        Assert.False(result.Value.WhatsApp);
        Assert.Empty(result.Value.WhatsAppCountries);
        Assert.Null(result.Value.WhatsAppNumber);
    }

    private static AccountService Service(bool google, bool whatsApp)
    {
        var loginCodeOptions = Options.Create(new LoginCodeOptions());
        var issuer = new LoginCodeIssuer(
            new InMemoryLoginCodeRepository(),
            new FakeLoginCodeGenerator(),
            new FakeLoginCodeHasher(),
            loginCodeOptions,
            Options.Create(Settings),
            TimeProvider.System,
            NullLogger<LoginCodeIssuer>.Instance);

        return new AccountService(
            new FakeGoogleAvailability(google),
            new FakeWhatsAppAvailability(whatsApp),
            Options.Create(Settings),
            issuer,
            new FakeIdentityService(),
            new FakeEmailTemplateRenderer(),
            new FakeEmailQueue(),
            new AccountCreationPolicy(new FakeSystemSettingsReader(), new FakeInitialAdmin()),
            loginCodeOptions,
            new ServiceRequestValidator<RequestLoginCodeRequest>([new RequestLoginCodeRequestValidator()]),
            new FakeUnitOfWork(),
            NullLogger<AccountService>.Instance);
    }
}
