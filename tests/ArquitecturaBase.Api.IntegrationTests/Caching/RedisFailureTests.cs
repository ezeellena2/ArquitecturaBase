using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Domain.Settings;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

[Collection(ApiTestGroup.Name)]
public sealed class RedisFailureTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Redis_outage_blocks_readiness_and_cached_reads_but_liveness_survives_and_the_client_recovers()
    {
        // Docker reassigns an ephemeral mapping on restart; a reserved host port keeps the client's endpoint stable.
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var redisPort = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        await using var redis = new RedisBuilder("redis:8.6").WithPortBinding(redisPort, 6379).Build();
        await redis.StartAsync(Ct);
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:cache", redis.GetConnectionString())
            .UseSetting("Caching:KeyPrefix", RedisCacheTests.NewPrefix()));
        using var client = api.CreateClient();
        using var warm = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        await redis.StopAsync(Ct);

        using var ready = await client.GetAsync("/health", Ct);
        using var live = await client.GetAsync("/alive", Ct);
        using var failed = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var problem = await failed.ReadJsonAsync();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("redis", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);

        await redis.StartAsync(Ct);
        using var recovery = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        recovery.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var health = await client.GetAsync("/health", recovery.Token);
            if (health.IsSuccessStatusCode)
            {
                break;
            }
            await Task.Delay(100, recovery.Token);
        }
        using var recovered = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task Failed_post_commit_invalidation_returns_a_generic_error_but_the_database_change_is_committed()
    {
        using var loginClient = factory.CreateClient();
        await loginClient.LoginAsync(factory, ApiFactory.AdminEmail);
        var adminId = await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == ApiFactory.AdminEmail)
            .Select(user => user.Id).SingleAsync(Ct));
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ISystemSettingsCache, FailingSystemSettingsCache>())));
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/settings", UriKind.Relative))
        {
            Content = JsonContent.Create(new { registrationMode = "Open" }),
        };
        // Separate test hosts use ephemeral token-protection keys. This test exercises the real permission service.
        request.Headers.Add(TestAuthHandler.UserIdHeader, adminId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(RegistrationMode.Open, await factory.ExecuteDbContextAsync(db => db.SystemSettings
            .AsNoTracking().Select(settings => settings.RegistrationMode).SingleAsync(Ct)));
    }
}
