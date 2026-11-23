using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// No host/DB/worker/provider: the real registration of the module with WhatsApp off. RC3-A (2026-11-22): the time to answer a form, the
// countries and the retention are Configuración › WhatsApp, read from the base on every use, never from appsettings.
public sealed class WhatsAppSettingsRegistrationTests
{
    [Fact]
    public void The_settings_are_read_from_the_base_with_whatsapp_disabled()
    {
        var services = new ServiceCollection();
        services.AddWhatsAppInfrastructure(new ConfigurationBuilder().Build());

        Assert.Contains(services, service => service.ServiceType == typeof(IWhatsAppSettingsReader) && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service => service.ServiceType == typeof(IWhatsAppSettingsRepository) && service.Lifetime == ServiceLifetime.Scoped);
        // Even off, the time to answer a form is the settings' one: the environment is scoped to read it.
        Assert.Contains(services, service => service.ServiceType == typeof(IWhatsAppFlowEnvironment) && service.Lifetime == ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    public void A_retention_left_in_appsettings_neither_counts_nor_stops_the_start(string configured)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsApp:MessageRetentionDays"] = configured,
            ["WhatsApp:ApplyMessageRetentionInBackground"] = "false",
        }).Build();
        var services = new ServiceCollection();
        services.AddWhatsAppInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IOptions<WhatsAppMessageRetentionOptions>>().Value.ApplyMessageRetentionInBackground);
    }
}
