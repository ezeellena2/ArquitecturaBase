using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// La Api termina lo suyo antes de que la plataforma la mate (N17): 25 segundos para apagarse, contra los 40 a 60 que la
/// Container App le da antes de cortarla (<c>terminationGracePeriodSeconds</c>, docs/deploy/azure-setup.md). Con los 30 de
/// los dos por defecto no quedaba margen.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ShutdownTimeoutTests(ApiFactory factory)
{
    [Fact]
    public void The_api_gives_itself_25_seconds_to_stop()
    {
        var options = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(25), options.ShutdownTimeout);
    }
}
