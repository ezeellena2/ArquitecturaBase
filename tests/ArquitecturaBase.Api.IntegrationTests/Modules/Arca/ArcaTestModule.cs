using ArquitecturaBase.Api.IntegrationTests.Support;
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

    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        foreach (var name in new[] { ArcaInfrastructureRegistration.WsaaHttpClientName, ArcaInfrastructureRegistration.RegistryHttpClientName,
                     ArcaInfrastructureRegistration.InvoicingHttpClientName })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Automated tests never call ARCA.");
    }
}
