using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Configuration;

/// <summary>La sección WhatsApp tal como la lee Application, con el registro real del módulo (AddWhatsAppApplication).</summary>
public sealed class WhatsAppLoginOptionsTests
{
    [Fact]
    public void Without_the_keys_the_daily_limit_is_100()
    {
        var options = Read(new());

        Assert.Equal(100, options.DailyAuthCodeLimit);
        Assert.Null(options.DisplayPhoneNumber);
    }

    [Fact]
    public void The_number_of_the_bot_is_read_as_digits_only()
    {
        var options = Read(new() { ["WhatsApp:DisplayPhoneNumber"] = "15551632662" });

        Assert.Equal("15551632662", options.DisplayPhoneNumber);
    }

    [Theory]
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
        services.AddWhatsAppApplication();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<WhatsAppLoginOptions>>().Value;
    }
}
