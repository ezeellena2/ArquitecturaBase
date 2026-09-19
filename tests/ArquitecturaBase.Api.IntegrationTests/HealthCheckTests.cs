using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArquitecturaBase.Api.IntegrationTests;

/// <summary>
/// Los probes del orquestador consultan estos endpoints fuera de Development: si no están mapeados, reciben 404 y
/// dan el contenedor por caído. La liveness no mira la base a propósito, para que una base caída no dispare
/// reinicios en cadena que no arreglan nada.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class HealthCheckTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_endpoint_responds_outside_development()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/alive", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_endpoint_responds_outside_development()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_covers_the_database_and_liveness_does_not()
    {
        var healthChecks = factory.Services.GetRequiredService<HealthCheckService>();

        var ready = await healthChecks.CheckHealthAsync(Ct);
        var live = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains("live"), Ct);

        Assert.Contains("database", ready.Entries.Keys);
        Assert.DoesNotContain("database", live.Entries.Keys);
    }

    /// <summary>La respuesta es una palabra, sin el detalle de cada check: no cuenta qué dependencias hay.</summary>
    [Fact]
    public async Task Health_response_does_not_leak_the_individual_checks()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal("Healthy", body);
    }
}
