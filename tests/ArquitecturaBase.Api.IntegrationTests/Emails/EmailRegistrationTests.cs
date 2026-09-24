using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

/// <summary>
/// La invitación por correo lleva un botón a la web, y la dirección de la web es el origen público
/// (<c>Authentication:Issuer</c>). Fuera de Development y Testing, sin esa dirección la Api no arranca: si no, la primera
/// invitación fallaría recién al mandarla, y el alta que la pedía tampoco quedaría. Corre el mismo validador que el host
/// antes de arrancar (<see cref="IStartupValidator"/>), como WhatsAppMessageRetentionTests: un despliegue de producción
/// entero necesitaría los certificados de OpenIddict.
/// </summary>
public sealed class EmailRegistrationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_and_testing_the_api_does_not_start_without_a_public_origin(string environment)
    {
        using var provider = BuildProvider(environment, issuer: null);

        var exception = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(EmailRegistration.MissingPublicOriginMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_public_origin_the_api_starts_in_production()
    {
        using var provider = BuildProvider("Production", issuer: "https://app.example.com/");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider BuildProvider(string environment, string? issuer)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Sin SMTP, que se valida aparte y pediría sus propias claves.
                ["Email:Delivery"] = "PickupDirectory",
                ["Authentication:Issuer"] = issuer,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IPublicOrigin, PublicOrigin>();
        services.AddEmails(new TestHostEnvironment { EnvironmentName = environment });

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
