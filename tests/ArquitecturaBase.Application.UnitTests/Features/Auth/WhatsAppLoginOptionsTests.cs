using ArquitecturaBase.Application.Features.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

/// <summary>La sección WhatsApp tal como la lee Application, con la registración real de AddApplication.</summary>
public sealed class WhatsAppLoginOptionsTests
{
    [Fact]
    public void Without_the_keys_only_argentina_is_allowed_and_the_daily_limit_is_100()
    {
        var options = Read(new());

        Assert.Equal(["AR"], options.Countries);
        Assert.Equal(100, options.DailyAuthCodeLimit);
        Assert.Null(options.DisplayPhoneNumber);
    }

    [Fact]
    public void Allowed_countries_from_the_configuration_replace_the_default()
    {
        // Sin repetir "AR": el binder suma a lo que la lista ya tenía, por eso la propiedad no arranca con el default.
        var options = Read(new()
        {
            ["WhatsApp:AllowedCountries:0"] = "AR",
            ["WhatsApp:AllowedCountries:1"] = "UY",
        });

        Assert.Equal(["AR", "UY"], options.Countries);
    }

    [Fact]
    public void The_number_of_the_bot_is_read_as_digits_only()
    {
        var options = Read(new() { ["WhatsApp:DisplayPhoneNumber"] = "15551632662" });

        Assert.Equal("15551632662", options.DisplayPhoneNumber);
    }

    [Theory]
    [InlineData("WhatsApp:AllowedCountries:0", "ar")]
    [InlineData("WhatsApp:AllowedCountries:0", "ARG")]
    [InlineData("WhatsApp:AllowedCountries:0", "A1")]
    [InlineData("WhatsApp:DailyAuthCodeLimit", "0")]
    [InlineData("WhatsApp:DisplayPhoneNumber", "+15551632662")]
    [InlineData("WhatsApp:DisplayPhoneNumber", "1 555 163 2662")]
    public void An_invalid_value_is_rejected_naming_its_key(string key, string value)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Read(new() { [key] = value }));

        var setting = key.Split(':')[1];
        Assert.Contains(exception.Failures, failure => failure.Contains(setting, StringComparison.Ordinal));
    }

    private static WhatsAppLoginOptions Read(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<WhatsAppLoginOptions>>().Value;
    }
}
