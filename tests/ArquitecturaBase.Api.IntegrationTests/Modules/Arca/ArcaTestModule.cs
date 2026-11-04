using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca;

internal sealed class ArcaTestModule : IApiTestModule
{
    public IReadOnlyCollection<string> Routes => [];

    public void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Arca:SecretProtection:InstallationId", "integration-installation");
        builder.UseSetting("Arca:SecretProtection:ActiveKeyId", "test");
        builder.UseSetting("Arca:SecretProtection:Keys:test", ApiFactory.TestHashKey);
    }

    // Los clientes de ARCA hablan con FakeArca, que no abre red y falla ante una operación que no conoce.
    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        services.AddSingleton(new FakeArca(factory.Clock));
        foreach (var name in new[] { ArcaInfrastructureRegistration.WsaaHttpClientName, ArcaInfrastructureRegistration.RegistryHttpClientName,
                     ArcaInfrastructureRegistration.InvoicingHttpClientName })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(provider => new FakeArcaHandler(provider.GetRequiredService<FakeArca>()));
        }
    }
}
