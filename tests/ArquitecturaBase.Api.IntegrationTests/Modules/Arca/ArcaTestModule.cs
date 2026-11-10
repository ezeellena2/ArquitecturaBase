using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca;

internal sealed class ArcaTestModule : IApiTestModule
{
    public IReadOnlyCollection<string> Routes =>
    [
        "GET /api/arca/connection", "POST /api/arca/connection/certificate-requests", "GET /api/arca/connection/certificate-requests/current",
        "DELETE /api/arca/connection/certificate-requests/current", "PUT /api/arca/connection/services", "PUT /api/arca/connection/certificate",
        "POST /api/arca/connection/test", "DELETE /api/arca/connection", "GET /api/arca/registry/company", "GET /api/arca/invoicing/settings",
        "GET /api/arca/invoicing/points-of-sale", "PUT /api/arca/invoicing/point-of-sale", "GET /api/arca/connection/robot-runs/current",
        "POST /api/arca/connection/robot-runs",
    ];

    public const string RobotAlertEmail = "robot-arca@ejemplo.com";

    public void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Arca:SecretProtection:InstallationId", "integration-installation");
        builder.UseSetting("Arca:SecretProtection:ActiveKeyId", "test");
        builder.UseSetting("Arca:SecretProtection:Keys:test", ApiFactory.TestHashKey);
        builder.UseSetting("Arca:Platform:Cuit", "30-71999888-3");
        builder.UseSetting("Arca:Platform:Name", "Plataforma Ejemplo S.A.");
        builder.UseSetting("Arca:RateLimiting:ReadPermitLimit", "100000");
        builder.UseSetting("Arca:RateLimiting:WritePermitLimit", "100000");
        builder.UseSetting("Arca:RateLimiting:RemotePermitLimit", "100000");
        // La cola de emisión la procesa cada test, con el reloj del factory.
        builder.UseSetting("Arca:Invoicing:Worker:ProcessInBackground", "false");
        // El robot prendido contra las páginas simuladas de ARCA; sus workers los corre cada test.
        builder.UseSetting("Arca:Robot:Enabled", "true");
        builder.UseSetting("Arca:Robot:ProcessInBackground", "false");
        builder.UseSetting("Arca:Robot:LoginUrl", FakeArcaWeb.Shared.LoginUrl.ToString());
        builder.UseSetting("Arca:Robot:StepTimeout", "00:00:05");
        builder.UseSetting("Arca:Robot:AlertEmail", RobotAlertEmail);
        builder.UseSetting("Arca:Robot:Platform:LoginCuit", "30-71999888-3");
        builder.UseSetting("Arca:Robot:Platform:FiscalKey", FakeArcaWeb.PlatformKey);
    }

    // Los clientes de ARCA hablan con FakeArca, que no abre red y falla ante una operación que no conoce.
    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        services.AddSingleton(new FakeArca(factory.Clock));
        services.AddSingleton(FakeArcaWeb.Shared);
        services.AddSingleton<FakeArcaLogCapture>();
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<FakeArcaLogCapture>());
        // El chequeo de Chromium levanta Playwright al arrancar: cada factory lo pagaría. Se prueba una vez, en ArcaDelegationRobotTests.
        services.Remove(services.Single(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(ArcaRobotBrowserCheck)));
        foreach (var name in new[] { ArcaInfrastructureRegistration.WsaaHttpClientName, ArcaInfrastructureRegistration.RegistryHttpClientName,
                     ArcaInfrastructureRegistration.InvoicingHttpClientName })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(provider => new FakeArcaHandler(provider.GetRequiredService<FakeArca>()));
        }
    }
}
