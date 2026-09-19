using ArquitecturaBase.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// El arnés de integración (ApiFactory) reemplaza el esquema por defecto por un handler de prueba, así que
/// ningún otro test cubre el registro real de autenticación (<c>AddInfrastructure</c>) que usa la Api en
/// producción. Sin Docker: arma el service provider a mano, con la configuración mínima que necesita para construirse.
/// </summary>
public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public async Task Default_scheme_is_the_openiddict_validation_scheme()
    {
        await using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>();

        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, options.Value.DefaultScheme);
    }

    [Fact]
    public async Task Identity_application_cookie_scheme_is_registered()
    {
        await using var provider = BuildProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemes.GetSchemeAsync(IdentityConstants.ApplicationScheme);

        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task Openiddict_validation_rejects_revoked_tokens_immediately()
    {
        await using var provider = BuildProvider();

        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIddictValidationOptions>>();

        // Refleja options.EnableTokenEntryValidation() en OpenIddictRegistration: sin ella, un token revocado
        // (logout o reuso de un refresh token) seguiría valiendo hasta que venciera.
        Assert.True(monitor.CurrentValue.EnableTokenEntryValidation);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Se lee de forma diferida (recién al crear un ApplicationDbContext): no hace falta una base real.
                ["ConnectionStrings:appdb"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration, new TestHostEnvironment());

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
