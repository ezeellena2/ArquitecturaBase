using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// No host/DB/worker/provider. Resolves only an immutable projection from the real registration.
public sealed class WhatsAppContentRetentionRegistrationTests
{
    [Theory]
    [InlineData(null, 90)]
    [InlineData("1", 1)]
    [InlineData("3650", 3650)]
    public void Application_receives_the_same_validated_days_while_whatsapp_is_disabled(string? configured, int expected)
    {
        var values = new Dictionary<string, string?>();
        if (configured is not null) values["WhatsApp:MessageRetentionDays"] = configured;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddWhatsAppInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetRequiredService<WhatsAppContentRetention>().MessageRetentionDays);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    public void The_projection_keeps_the_existing_options_validation_as_its_single_source(string configured)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsApp:MessageRetentionDays"] = configured,
        }).Build();
        var services = new ServiceCollection();
        services.AddWhatsAppInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<WhatsAppContentRetention>());
    }
}
