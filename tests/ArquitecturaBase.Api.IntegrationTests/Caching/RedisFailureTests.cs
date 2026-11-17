using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

[Collection(ApiTestGroup.Name)]
public sealed class RedisFailureTests(ApiFactory factory)
{
    private static readonly TimeSpan CachedHealthWindow = TimeSpan.FromSeconds(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// N9 (2026-11-16): con Redis caído la app sigue. /health da Degraded, que responde 200, así que la copia sigue en el
    /// tráfico; el ingreso, los permisos y la configuración se leen de la base; y el seed del arranque no se frena. Cuando
    /// Redis vuelve, el cliente reconecta solo.
    /// </summary>
    [Fact]
    public async Task With_redis_down_the_app_keeps_working_degraded_and_recovers_when_it_returns()
    {
        // Docker reassigns an ephemeral mapping on restart; a reserved host port keeps the client's endpoint stable.
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var redisPort = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        await using var redis = new RedisBuilder("redis:8.6").WithPortBinding(redisPort, 6379).Build();
        await redis.StartAsync(Ct);
        var email = await CreateUserWithRolesReadAsync();
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:cache", redis.GetConnectionString())
            .UseSetting("Caching:KeyPrefix", RedisCacheTests.NewPrefix()));
        using var client = api.CreateClient();
        using var warm = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        await redis.StopAsync(Ct);
        factory.Clock.Advance(CachedHealthWindow);

        using var ready = await client.GetAsync("/health", Ct);
        using var live = await client.GetAsync("/alive", Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // La configuración, de la base: el modo de registro y la presentación.
        var registrationOpen = await factory.ExecuteDbContextAsync(db => db.SystemSettings.AsNoTracking()
            .Select(settings => settings.RegistrationMode == RegistrationMode.Open).SingleAsync(Ct));
        using var methods = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
        Assert.Equal(registrationOpen, (await methods.ReadJsonAsync()).GetProperty("registrationOpen").GetBoolean());
        using var presentation = await client.GetAsync("/api/settings/presentation", Ct);
        Assert.Equal(HttpStatusCode.OK, presentation.StatusCode);

        // El ingreso entero y una ruta con permiso.
        var tokens = await client.LoginAsync(factory, email);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        using var roles = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);
        using var users = await client.GetWithTokenAsync("/api/users", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);

        // El arranque siembra y descarta en Redis la configuración: sin Redis, siembra igual.
        await api.Services.SeedDatabaseAsync(Ct);

        await redis.StartAsync(Ct);
        using var recovery = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        recovery.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            // /health guarda su resultado unos segundos (N6), medidos con el reloj del arnés.
            factory.Clock.Advance(CachedHealthWindow);
            using var health = await client.GetAsync("/health", recovery.Token);
            if (await health.Content.ReadAsStringAsync(recovery.Token) == "Healthy")
            {
                break;
            }
            await Task.Delay(100, recovery.Token);
        }
        using var recovered = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    /// <summary>
    /// Revisión adversarial de N9: con Redis colgado (el socket abierto y sin respuesta, no caído) ningún pedido da 500.
    /// Una cancelación que no pidió quien llama (el cliente cancela los comandos pendientes cuando suelta el socket muerto)
    /// cuenta como Redis no disponible, y los ajustes salen de la base. Cada lectura así deja un renglón: un Warning por
    /// proceso y por minuto, con la excepción, y el resto en Debug, sin ella.
    /// </summary>
    [Fact]
    public async Task With_redis_hung_the_settings_come_from_the_database_without_a_500()
    {
        await using var redis = new RedisBuilder("redis:8.6").Build();
        await redis.StartAsync(Ct);
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:cache", redis.GetConnectionString())
            .UseSetting("Caching:KeyPrefix", RedisCacheTests.NewPrefix())
            .UseSetting("Logging:LogLevel:" + typeof(SystemSettingsReader).FullName, "Debug")
            .ConfigureTestServices(services => services.AddLogging(logging => logging.AddFakeLogging())));
        using var client = api.CreateClient();
        using var warm = await client.GetAsync("/account/login-methods", Ct);
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        await redis.PauseAsync(Ct);
        try
        {
            for (var i = 0; i < 6; i++)
            {
                using var methods = await client.GetAsync("/account/login-methods", Ct);
                using var presentation = await client.GetAsync("/api/settings/presentation", Ct);
                Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
                Assert.Equal(HttpStatusCode.OK, presentation.StatusCode);
            }

            // El descarte del seed tampoco frena el arranque con Redis colgado.
            await api.Services.SeedDatabaseAsync(Ct);
        }
        finally
        {
            await redis.UnpauseAsync(Ct);
        }

        // La ventana es del proceso: otro test pudo haber dejado el Warning de este minuto.
        var fallbacks = api.Services.GetFakeLogCollector().GetSnapshot()
            .Where(log => log.Category == typeof(SystemSettingsReader).FullName).ToList();
        Assert.InRange(fallbacks.Count(log => log.Level == LogLevel.Warning), 0, 1);
        var quiet = fallbacks.Where(log => log.Level == LogLevel.Debug).ToList();
        Assert.True(quiet.Count >= 11, $"Only {quiet.Count} reads went to Debug.");
        Assert.All(quiet, log => Assert.Null(log.Exception));
        Assert.Equal(fallbacks.Count, quiet.Count + fallbacks.Count(log => log.Level == LogLevel.Warning));
    }

    /// <summary>
    /// Con Redis colgado, al soltar el socket muerto el cliente puede fallar el comando que esperaba con la excepción de su
    /// pipe (InvalidOperationException) en vez de una RedisConnectionException: es una carrera que dio un 500
    /// en el test de arriba. También es Redis que no responde: cada operación pública lo dice así, y los ajustes salen de la
    /// base.
    /// </summary>
    [Fact]
    public async Task A_command_the_client_fails_while_dropping_a_hung_socket_counts_as_redis_not_answering()
    {
        var cache = new RedisCache(
            DroppedSocketRedis.Create(),
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RedisCacheOptions { KeyPrefix = RedisCacheTests.NewPrefix() }),
            NullLogger<RedisCache>.Instance);
        var key = new RedisCacheKey("dropped-socket");
        Func<Task>[] operations =
        [
            () => cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, int>(
                key, 0, static (_, _, _) => Task.FromResult(1), TimeSpan.FromMinutes(1), Ct),
            () => cache.RemoveAsync(key, Ct),
            () => cache.TryAcquireExclusiveLeaseAsync(key, "owner", TimeSpan.FromSeconds(1), Ct),
            () => cache.RenewExclusiveLeaseAsync(key, "owner", TimeSpan.FromSeconds(1), Ct),
            () => cache.ReleaseExclusiveLeaseAsync(key, "owner", TimeSpan.Zero, Ct),
        ];

        foreach (var operation in operations)
        {
            Assert.True(RedisCache.IsUnavailable(await Assert.ThrowsAnyAsync<Exception>(operation)));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reader = new SystemSettingsReader(cache, db, NullLogger<SystemSettingsReader>.Instance);
        var stored = await db.SystemSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(stored.RegistrationMode, await reader.FindRegistrationModeAsync(Ct));
        Assert.Equal(stored.DefaultCulture, (await reader.FindPresentationAsync(Ct)).DefaultCulture);
    }

    /// <summary>
    /// La ventana del Warning de <see cref="SystemSettingsReader"/>: la primera lectura sin Redis la abre, las del mismo
    /// minuto no, y la primera después de un minuto abre otra.
    /// </summary>
    [Fact]
    public void The_settings_fallback_warns_once_per_minute()
    {
        long last = 0;
        var start = Stopwatch.GetTimestamp();
        var almostAMinute = start + (long)(Stopwatch.Frequency * 59.9);
        var aMinute = start + Stopwatch.Frequency * 60;

        Assert.True(SystemSettingsReader.TryOpenWarningWindow(ref last, start, TimeSpan.FromMinutes(1)));
        Assert.False(SystemSettingsReader.TryOpenWarningWindow(ref last, start, TimeSpan.FromMinutes(1)));
        Assert.False(SystemSettingsReader.TryOpenWarningWindow(ref last, almostAMinute, TimeSpan.FromMinutes(1)));
        Assert.True(SystemSettingsReader.TryOpenWarningWindow(ref last, aMinute, TimeSpan.FromMinutes(1)));
        Assert.Equal(aMinute, last);
        Assert.False(SystemSettingsReader.TryOpenWarningWindow(ref last, aMinute + 1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Failed_post_commit_invalidation_returns_a_generic_error_but_the_database_change_is_committed()
    {
        using var loginClient = factory.CreateClient();
        await loginClient.LoginAsync(factory, ApiFactory.AdminEmail);
        var adminId = await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == ApiFactory.AdminEmail)
            .Select(user => user.Id).SingleAsync(Ct));
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var revision = await factory.ExecuteDbContextAsync(db => db.SystemSettings.AsNoTracking()
            .Select(settings => settings.Revision).SingleAsync(Ct));
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ISystemSettingsCache, FailingSystemSettingsCache>())));
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/settings", UriKind.Relative))
        {
            Content = JsonContent.Create(new { expectedRevision = revision, registrationMode = "Open" }),
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

    /// <summary>Una cuenta nueva con un rol que sólo da «Ver roles»; devuelve su correo.</summary>
    private Task<string> CreateUserWithRolesReadAsync() =>
        factory.InTransactionAsync(async services =>
        {
            var suffix = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var role = "redis-down-" + suffix;
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, [Permissions.Roles.Read], Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var email = Email.Create("redis-down-" + suffix + "@example.com").Value;
            var user = await users.CreateAsync(email, phone: null, phoneConfirmed: false, displayName: null, "es", Ct);
            await users.SetRolesAsync(user.Id, [role], Ct);

            return email.Value;
        });
}
