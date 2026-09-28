using System.Reflection;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// El seed corre en un solo límite y en fila entre réplicas, con el lock "seed:database" (Etapa 7, tarea 8). Cada test usa
/// una base propia, migrada con el ApplicationDbContext de producción y vacía: la de la Api compartida ya está sembrada.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class DatabaseSeederTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_seed_waits_for_the_seed_lock()
    {
        await using var api = WithNewDatabase(factory);
        await using var scope = api.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var dbContext = new ApplicationDbContext(options);

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            Task seed;

            // Otra conexión, como otra réplica que está sembrando, toma el lock y no lo suelta hasta cerrar su transacción.
            // El texto va escrito acá y no sale de AdvisoryLockKeys: es lo que pediría una versión anterior de la Api.
            await using (var holder = new ApplicationDbContext(options))
            await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
            {
                await holder.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended('seed:database', 0))", Ct);

                seed = Task.Run(() => api.Services.SeedDatabaseAsync(Ct), Ct);

                // Determinista: sin el lock, el seed termina solo y el test falla acá; con el lock, espera en Postgres.
                await WaitUntilTheSeedWaitsForALockAsync(dbContext, seed);
                Assert.False(seed.IsCompleted, "The seed finished while another connection held the seed lock.");
                Assert.False(await dbContext.Roles.AnyAsync(Ct));
                Assert.False(await dbContext.SystemSettings.AnyAsync(Ct));

                await transaction.RollbackAsync(Ct);
            }

            // Suelto el lock, termina y deja todo.
            await seed.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            await AssertSeededOnceAsync(dbContext);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Two_seeds_in_parallel_leave_one_row_of_each()
    {
        // Dos réplicas que arrancan juntas. Sin el lock, el rojo es probabilístico (chocan los índices únicos de los roles,
        // del scope o del cliente si se cruzan): el rojo seguro es The_seed_waits_for_the_seed_lock.
        await using var api = WithNewDatabase(factory);
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            await Task.WhenAll(
                    Task.Run(() => api.Services.SeedDatabaseAsync(Ct), Ct),
                    Task.Run(() => api.Services.SeedDatabaseAsync(Ct), Ct))
                .WaitAsync(TimeSpan.FromSeconds(60), Ct);

            await AssertSeededOnceAsync(dbContext);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task A_failing_seed_leaves_nothing()
    {
        // El cliente web es lo último: cuando falla, los roles, la fila de ajustes y el scope ya se guardaron adentro de
        // la transacción. No sirve fallar con opciones inválidas, porque ValidateOnStart corta el arranque antes del seed.
        await using var api = WithNewDatabase(factory, builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOpenIddictApplicationManager>();
            services.AddScoped(_ => FailingApplicationManager.Create());
        }));
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            await Assert.ThrowsAsync<ExpectedSeedFailure>(() => api.Services.SeedDatabaseAsync(Ct));

            Assert.False(await dbContext.Roles.AnyAsync(Ct));
            Assert.False(await dbContext.RoleClaims.AnyAsync(Ct));
            Assert.False(await dbContext.SystemSettings.AnyAsync(Ct));
            Assert.False(await dbContext.Set<OpenIddictEntityFrameworkCoreScope<Guid>>().AnyAsync(Ct));
            Assert.False(await dbContext.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().AnyAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    /// <summary>La Api del arnés sobre una base nueva y vacía, que el test migra. EF la crea al migrar.</summary>
    private static WebApplicationFactory<Program> WithNewDatabase(ApiFactory factory, Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("seed"));
            configure?.Invoke(builder);
        });

    /// <summary>
    /// Espera a que alguien quede esperando un advisory lock en esta base, o a que el seed termine. Si termina, es que no
    /// esperó el lock, y el test lo dice.
    /// </summary>
    private static async Task WaitUntilTheSeedWaitsForALockAsync(ApplicationDbContext observer, Task seed)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!seed.IsCompleted
            && await observer.Database
                .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = 'advisory'""")
                .SingleAsync(timeout.Token) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    /// <summary>Admin con todos los permisos y User, una fila de ajustes, el scope "api" y el cliente "web", una vez cada uno.</summary>
    private static async Task AssertSeededOnceAsync(ApplicationDbContext dbContext)
    {
        var roles = await dbContext.Roles.AsNoTracking().Select(role => role.Name!).ToListAsync(Ct);
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], roles.Order(StringComparer.Ordinal));

        // Sin Distinct: un permiso repetido también es un rojo.
        var adminPermissions = await dbContext.RoleClaims
            .Where(claim => claim.ClaimType == Permissions.ClaimType
                && dbContext.Roles.Any(role => role.Id == claim.RoleId && role.Name == SystemRoles.Admin))
            .Select(claim => claim.ClaimValue!)
            .ToListAsync(Ct);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), adminPermissions.Order(StringComparer.Ordinal));

        Assert.Equal(1, await dbContext.SystemSettings.CountAsync(Ct));
        Assert.Equal(1, await dbContext.Set<OpenIddictEntityFrameworkCoreScope<Guid>>()
            .CountAsync(scope => scope.Name == AuthServerDefaults.ApiScope, Ct));
        Assert.Equal(1, await dbContext.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .CountAsync(application => application.ClientId == AuthServerDefaults.WebClientId, Ct));
    }
}

/// <summary>La falla que simula el manager de aplicaciones de OpenIddict.</summary>
internal sealed class ExpectedSeedFailure : Exception;

/// <summary>
/// Un IOpenIddictApplicationManager que lanza <see cref="ExpectedSeedFailure"/> en cualquier llamada. Es un
/// <see cref="DispatchProxy"/> para no escribir a mano los métodos de la interfaz; es pública y no está sellada porque el
/// proxy se arma heredando de ella, como <see cref="StaleUserReads"/>.
/// </summary>
public class FailingApplicationManager : DispatchProxy
{
    public static IOpenIddictApplicationManager Create() => Create<IOpenIddictApplicationManager, FailingApplicationManager>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new ExpectedSeedFailure();
}
