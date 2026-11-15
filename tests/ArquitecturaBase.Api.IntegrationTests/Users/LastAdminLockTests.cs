using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Desactivar, eliminar o sacarle el rol Admin a un administrador activo cuenta antes a los administradores activos, y
/// ese conteo va en fila con el lock global "users:admins": sin él, dos administradores que se desactivan entre sí a la
/// vez cuentan dos cada uno y dejan el sistema sin administradores. Cada test usa una base propia, migrada, sembrada y
/// con solo sus cuentas: la de la Api compartida tiene otros administradores. Los casos de uso se llaman directo, sin
/// sesión (el usuario actual es null, así que ninguno es "uno mismo").
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class LastAdminLockTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Removals => ["deactivate", "delete", "remove-admin-role"];

    [Theory]
    [MemberData(nameof(Removals))]
    public async Task Removing_an_active_admin_waits_for_the_admins_lock(string removal)
    {
        await using var api = WithNewDatabase(factory);
        var options = api.Services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var observer = new ApplicationDbContext(options);

        try
        {
            await PrepareAsync(api, observer);
            var first = await CreateAdminAsync(api, "primero");
            await CreateAdminAsync(api, "segundo");

            Task<Result> removing;

            // Otra conexión, como otro administrador que está desactivando a alguien, toma el lock y no lo suelta hasta
            // cerrar su transacción. El texto va escrito acá y no sale de AdvisoryLockKeys: es lo que pediría una versión
            // anterior de la Api.
            await using (var holder = new ApplicationDbContext(options))
            await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
            {
                await holder.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended('users:admins', 0))", Ct);

                removing = Task.Run(() => RemoveAsync(api, removal, first), Ct);

                // Determinista: sin el lock, el caso de uso termina solo y el test falla acá; con el lock, espera en
                // Postgres, justamente por esa clave.
                await WaitUntilItWaitsForTheAdminsLockAsync(observer, removing);
                Assert.False(removing.IsCompleted, "The use case finished while another connection held the admins lock.");

                await transaction.RollbackAsync(Ct);
            }

            var result = await removing.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            Assert.Equal(1, await CountActiveAdminsAsync(api));
        }
        finally
        {
            await observer.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Two_admins_deactivating_each_other_at_once_leave_exactly_one()
    {
        await using var api = WithNewDatabase(factory);
        await using var observer = new ApplicationDbContext(
            api.Services.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await PrepareAsync(api, observer);
            var ana = await CreateAdminAsync(api, "ana");
            var beto = await CreateAdminAsync(api, "beto");

            var results = await Task.WhenAll(
                    Task.Run(() => RemoveAsync(api, "deactivate", ana), Ct),
                    Task.Run(() => RemoveAsync(api, "deactivate", beto), Ct))
                .WaitAsync(TimeSpan.FromSeconds(60), Ct);

            Assert.Single(results, result => result.IsSuccess);
            Assert.Single(results, result => result.IsFailure && result.Error.Code == UserErrors.LastAdmin.Code);
            Assert.Equal(1, await CountActiveAdminsAsync(api));
        }
        finally
        {
            await observer.Database.EnsureDeletedAsync(Ct);
        }
    }

    private static WebApplicationFactory<Program> WithNewDatabase(ApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("admins")));

    private static async Task PrepareAsync(WebApplicationFactory<Program> api, ApplicationDbContext dbContext)
    {
        await dbContext.Database.MigrateAsync(Ct);
        await api.Services.SeedDatabaseAsync(Ct);
    }

    private static async Task<Guid> CreateAdminAsync(WebApplicationFactory<Program> api, string name)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var id = Guid.Empty;

        var created = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async ct =>
            {
                var account = await repository.CreateAsync(
                    Email.Create(TestEmails.Unique(name)).Value, phone: null, phoneConfirmed: false, name, "es", ct);
                await repository.SetRolesAsync(account.Id, [SystemRoles.Admin], ct);
                id = account.Id;
                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            Ct);
        Assert.True(created.IsSuccess);

        return id;
    }

    private static async Task<Result> RemoveAsync(WebApplicationFactory<Program> api, string removal, Guid userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        return removal switch
        {
            "deactivate" => await services.GetRequiredService<IUserAccessService>()
                .SetUserActiveAsync(userId, isActive: false, Ct),
            "delete" => await services.GetRequiredService<IUserAccessService>().DeleteUserAsync(userId, Ct),
            "remove-admin-role" => await services.GetRequiredService<IUserAdministrationService>()
                .UpdateUserAsync(new UpdateUserRequest(userId, "Sin admin", [SystemRoles.User]), Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(removal), removal, null),
        };
    }

    private static async Task<int> CountActiveAdminsAsync(WebApplicationFactory<Program> api)
    {
        await using var scope = api.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IUserReader>().CountActiveAdminsAsync(Ct);
    }

    /// <summary>
    /// Espera a que alguien quede esperando justo el lock "users:admins", o a que el caso de uso termine. Si termina, es
    /// que no lo esperó, y el test lo dice. pg_locks parte la clave de 64 bits en classid (la mitad alta) y objid.
    /// </summary>
    private static async Task WaitUntilItWaitsForTheAdminsLockAsync(ApplicationDbContext observer, Task removing)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!removing.IsCompleted
            && await observer.Database
                .SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM pg_locks
                    WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1
                      AND ((classid::bigint << 32) | objid::bigint) = hashtextextended('users:admins', 0)
                    """)
                .SingleAsync(timeout.Token) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
