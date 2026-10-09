using ArquitecturaBase.Api.IntegrationTests.Contracts;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

internal sealed class WhatsAppTestModule : IApiTestModule
{
    public void ConfigureSettings(IWebHostBuilder builder) => ApiFactory.ConfigureWhatsAppSettings(builder);
    public void ConfigureServices(ApiFactory factory, IServiceCollection services) => factory.ConfigureWhatsAppServices(services);
    public IReadOnlyCollection<string> Routes => ExplicitRouteInventoryTests.WhatsAppRoutes;
}
