using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Pdf;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Robot;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        AddInvoicing(services, configuration);
        return services;
    }

    private static void AddConnection(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArcaSecretProtectionOptions>().Bind(configuration.GetSection("Arca:SecretProtection"))
            .Validate(x => x.IsValid(), "Configure Arca:SecretProtection with an installation ID, active key ID and external 256-bit keys.").ValidateOnStart();
        services.AddOptions<ArcaOptions>().Bind(configuration.GetSection("Arca"))
            .Validate(x => x.IsValid(), "Configure Arca with HTTPS addresses, the certificate issuer and the ticket retry window of each environment.").ValidateOnStart();
        services.AddSingleton<ArcaKeyRing>();
        services.AddSingleton<IArcaCredentialService, ArcaCredentialService>();
        services.AddSingleton<ArcaAuthClient>();
        services.AddSingleton<IArcaAccessTicketProvider, ArcaAccessTicketProvider>();
        services.AddScoped<IArcaConnectionRepository, ArcaConnectionRepository>();
        services.AddScoped<IArcaConnectionReader, ArcaConnectionReader>();
        ConfigureClient(services.AddHttpClient(WsaaHttpClientName));
        AddRobot(services, configuration);
    }

    /// El robot de delegación (ADR 0026): registrado siempre, pero sus workers y el chequeo de Chromium sólo corren prendido.
    private static void AddRobot(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArcaRobotBrowserOptions>().Bind(configuration.GetSection("Arca:Robot"))
            .Validate(x => x.IsValid(), "With Arca:Robot:Enabled, configure an HTTPS login page, positive timeouts, a queue of 1 to 50 and the platform login CUIT and tax key.")
            .ValidateOnStart();
        services.AddScoped<IArcaRobotRunRepository, ArcaRobotRunRepository>();
        services.AddScoped<IArcaRobotRunReader, ArcaRobotRunReader>();
        services.AddSingleton<ArcaCompanyRobotQueue>();
        services.AddSingleton<IArcaCompanyRobotQueue>(provider => provider.GetRequiredService<ArcaCompanyRobotQueue>());
        services.AddSingleton<IArcaDelegationRobot, PlaywrightArcaDelegationRobot>();
        services.AddSingleton<ArcaRobotProcessor>();
        services.AddHostedService<ArcaRobotBrowserCheck>();
        services.AddHostedService<ArcaCompanyRobotBackgroundService>();
        services.AddHostedService<ArcaPlatformRobotBackgroundService>();
    }

    private static void AddRegistry(IServiceCollection services)
    {
        services.AddScoped<IArcaRegistryRepository, ArcaRegistryRepository>();
        services.AddScoped<IArcaRegistryReader, ArcaRegistryReader>();
        services.AddSingleton<IArcaRegistryClient, ArcaRegistryClient>();
        ConfigureClient(services.AddHttpClient(RegistryHttpClientName));
    }

    private static void AddInvoicing(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArcaVoucherWorkerOptions>().Bind(configuration.GetSection("Arca:Invoicing:Worker"))
            .Validate(x => x.IsValid(), "Configure Arca:Invoicing:Worker with a positive interval and lease and a batch of 1 to 500.").ValidateOnStart();
        services.AddScoped<IArcaInvoicingRepository, ArcaInvoicingRepository>();
        services.AddScoped<IArcaInvoicingReader, ArcaInvoicingReader>();
        services.AddScoped<IArcaVoucherRepository, ArcaVoucherRepository>();
        services.AddScoped<IArcaVoucherReader, ArcaVoucherReader>();
        services.AddSingleton<IArcaInvoicingClient, ArcaInvoicingClient>();
        services.AddSingleton<IArcaFiscalPdfRenderer, ArcaFiscalPdfRenderer>();
        services.AddSingleton<ArcaVoucherProcessor>();
        services.AddHostedService<ArcaVoucherBackgroundService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArcaRobotCompanyStep, ArcaPointOfSaleRobotStep>());
        ConfigureClient(services.AddHttpClient(InvoicingHttpClientName));
    }

    // Los plazos los pone cada pedido con ArcaOptions.RequestTimeout; el cliente no reintenta, no sigue redirecciones ni loguea.
    private static void ConfigureClient(IHttpClientBuilder client) => client
        .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan)
        .RemoveAllLoggers().RemoveAllResilienceHandlers()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
}
