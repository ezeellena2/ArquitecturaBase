using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Infrastructure.Modules.Arca;

/// Una línea por familia de servicios (ADR 0025): quitar una familia es borrar sus carpetas y su línea.
public static class ArcaInfrastructureRegistration
{
    public const string WsaaHttpClientName = "ArcaWsaa";
    public const string RegistryHttpClientName = "ArcaRegistry";
    public const string InvoicingHttpClientName = "ArcaInvoicing";

    public static IServiceCollection AddArcaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(ArcaKeyRing))) return services;
        AddConnection(services, configuration);
        AddRegistry(services);
        AddInvoicing(services);
        return services;
    }

    private static void AddConnection(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArcaSecretProtectionOptions>().Bind(configuration.GetSection("Arca:SecretProtection"))
            .Validate(x => x.IsValid(), "Configure Arca:SecretProtection with an installation ID, active key ID and external 256-bit keys.").ValidateOnStart();
        services.AddOptions<ArcaOptions>().Bind(configuration.GetSection("Arca"))
            .Validate(x => x.IsValid(), "Configure Arca with HTTPS addresses, the certificate issuer and the ticket retry window of each environment.").ValidateOnStart();
        services.AddSingleton<ArcaKeyRing>();
        services.AddScoped<IArcaConnectionRepository, ArcaConnectionRepository>();
        services.AddScoped<IArcaConnectionReader, ArcaConnectionReader>();
        ConfigureClient(services.AddHttpClient(WsaaHttpClientName));
    }

    private static void AddRegistry(IServiceCollection services)
    {
        services.AddScoped<IArcaRegistryRepository, ArcaRegistryRepository>();
        services.AddScoped<IArcaRegistryReader, ArcaRegistryReader>();
        ConfigureClient(services.AddHttpClient(RegistryHttpClientName));
    }

    private static void AddInvoicing(IServiceCollection services)
    {
        services.AddScoped<IArcaInvoicingRepository, ArcaInvoicingRepository>();
        services.AddScoped<IArcaInvoicingReader, ArcaInvoicingReader>();
        ConfigureClient(services.AddHttpClient(InvoicingHttpClientName));
    }

    // Los plazos los pone cada pedido con ArcaOptions.RequestTimeout; el cliente no reintenta, no sigue redirecciones ni loguea.
    private static void ConfigureClient(IHttpClientBuilder client) => client
        .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan)
        .RemoveAllLoggers().RemoveAllResilienceHandlers()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
}
