using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

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
        Assert.Contains("StackExchange.Redis", ready.Entries.Keys);
        Assert.DoesNotContain("StackExchange.Redis", live.Entries.Keys);
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

    /// <summary>
    /// N6: el chequeo de la base corre un <c>SELECT 1</c> con una conexión del pool, que queda abierta para el próximo, y no
    /// abre una conexión nueva por consulta, que con una ráfaga a /health agotaría las del servidor. En Npgsql 10 el
    /// <c>CanConnectAsync</c> del chequeo de EF ya lo hace así; esto lo fija. La cadena lleva un nombre de aplicación propio,
    /// así que nadie más usa ese pool.
    /// </summary>
    [Fact]
    public async Task The_database_check_runs_on_a_pooled_connection()
    {
        var applicationName = "health-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { ApplicationName = applicationName }
            .ConnectionString;
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
        var registration = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(entry => entry.Name == "database");
        var services = new WithContext(factory.Services, context);

        try
        {
            var backends = new List<int>();
            for (var run = 0; run < 3; run++)
            {
                var result = await registration.Factory(services)
                    .CheckHealthAsync(new HealthCheckContext { Registration = registration }, Ct);
                Assert.Equal(HealthStatus.Healthy, result.Status);
                backends.Add(Assert.Single(await IdleCheckBackendsAsync(applicationName)));
            }

            // La misma conexión, abierta, las tres veces.
            Assert.Single(backends.Distinct());
        }
        finally
        {
            await using var pooled = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(pooled);
        }
    }

    /// <summary>
    /// N6: /health guarda su resultado dos segundos, y una ráfaga que llega mientras se calcula espera ese mismo cálculo:
    /// la base y Redis reciben una consulta por ventana, no una por pedido. /alive no toca dependencias y no se guarda.
    /// </summary>
    [Fact]
    public async Task Readiness_runs_its_checks_once_per_window_even_under_a_burst()
    {
        var counting = new CountingHealthCheck();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("counting", counting)));
        using var client = api.CreateClient();

        var burst = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("/health", Ct)));
        using var again = await client.GetAsync("/health", Ct);
        factory.Clock.Advance(TimeSpan.FromSeconds(2));
        using var later = await client.GetAsync("/health", Ct);

        Assert.All(burst, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in burst) response.Dispose();
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Equal(2, counting.Runs);
    }

    /// <summary>Las conexiones de ese pool que quedaron abiertas después de correr el chequeo.</summary>
    private async Task<List<int>> IdleCheckBackendsAsync(string applicationName)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_stat_activity WHERE application_name = @name AND state = 'idle' AND query = 'SELECT 1'",
            connection);
        command.Parameters.AddWithValue("name", applicationName);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var backends = new List<int>();
        while (await reader.ReadAsync(Ct))
        {
            backends.Add(reader.GetInt32(0));
        }

        return backends;
    }

    /// <summary>Los servicios de la Api, con el contexto del test en lugar del suyo.</summary>
    private sealed class WithContext(IServiceProvider services, ApplicationDbContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ApplicationDbContext) ? context : services.GetService(serviceType);
    }

    private sealed class CountingHealthCheck : IHealthCheck
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _runs);

            // Tarda un poco, como una consulta: la ráfaga llega mientras corre.
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

            return HealthCheckResult.Healthy();
        }
    }
}
