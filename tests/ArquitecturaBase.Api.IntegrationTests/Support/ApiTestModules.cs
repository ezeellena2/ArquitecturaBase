using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal interface IApiTestModule
{
    void ConfigureSettings(IWebHostBuilder builder);
    void ConfigureServices(ApiFactory factory, IServiceCollection services);
    IReadOnlyCollection<string> Routes { get; }
}

internal static class ApiTestModules
{
    public static IReadOnlyCollection<IApiTestModule> All { get; } =
        [.. typeof(ApiTestModules).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && typeof(IApiTestModule).IsAssignableFrom(x))
            .OrderBy(x => x.FullName, StringComparer.Ordinal)
            .Select(x => (IApiTestModule)Activator.CreateInstance(x, nonPublic: true)!)];
}
