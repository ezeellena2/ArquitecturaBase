using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Security;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Phones;
using ArquitecturaBase.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la base en el AppHost: Aspire inyecta ConnectionStrings:appdb.</summary>
    public const string DatabaseConnectionName = "appdb";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddSingleton(TimeProvider.System);

        // La base: contexto, interceptores, health check, unidad de trabajo, repositorios, lectores y seeders.
        services.AddPersistence(configuration);

        services.AddScoped<IOpenIddictTokenRevoker, OpenIddictTokenRevoker>();

        services.AddOptions<LoginCodeHashOptions>()
            .BindConfiguration(LoginCodeHashOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ILoginCodeGenerator, LoginCodeGenerator>();
        services.AddSingleton<ILoginCodeHasher, LoginCodeHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();

        // Sin estado propio: usa la instancia única de libphonenumber, que carga las reglas de cada país una sola vez.
        services.AddSingleton<IPhoneNumberParser, LibPhoneNumberParser>();

        services.AddIdentityServices(configuration);
        services.AddOpenIddictServer(configuration, environment);

        // El issuer de OpenIddict, para las direcciones que se arman sin un pedido del navegador (el enlace del bot).
        services.AddSingleton<IPublicOrigin, PublicOrigin>();

        services.AddEmails(environment);

        // Apagado sin WhatsApp:PhoneNumberId, como Google sin su ClientId: la app arranca igual.
        services.AddWhatsApp(configuration);

        return services;
    }
}
