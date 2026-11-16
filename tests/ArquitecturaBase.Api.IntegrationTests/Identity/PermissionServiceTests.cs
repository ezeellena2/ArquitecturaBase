using System.Data.Common;
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>
/// Permisos efectivos (N9 y N33, 2026-11-16): la suma de los permisos de los roles de la cuenta, leída de la base con una
/// sola consulta en el contexto del pedido, sin caché, y memorizada en ese scope. Los datos se arman como los arma la
/// aplicación: con IUserRepository e IRoleRepository, adentro de un límite (factory.InTransactionAsync).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PermissionServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_permission_service_is_one_instance_per_scope()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        Assert.Same(permissions, scope.ServiceProvider.GetRequiredService<IPermissionService>());

        await using var otherScope = factory.Services.CreateAsyncScope();
        Assert.NotSame(permissions, otherScope.ServiceProvider.GetRequiredService<IPermissionService>());
    }

    [Fact]
    public async Task Permissions_are_the_union_of_the_user_roles()
    {
        var userId = await CreateUserAsync(SystemRoles.Admin, SystemRoles.User);

        var permissions = await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct));

        Assert.Equal(factory.Services.GetRequiredService<IPermissionCatalog>().All.Order(StringComparer.Ordinal), permissions);
    }

    [Fact]
    public async Task User_role_has_no_permissions_yet()
    {
        var userId = await CreateUserAsync(SystemRoles.User);

        Assert.Empty(await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct)));
    }

    /// <summary>
    /// Una política pregunta varias veces en el mismo pedido: la base recibe una consulta por persona, no una por pregunta,
    /// y ninguna por rol.
    /// </summary>
    [Fact]
    public async Task One_query_per_scope_and_person_however_many_times_it_is_asked()
    {
        var first = await CreateUserAsync(SystemRoles.Admin, SystemRoles.User);
        var second = await CreateUserAsync(SystemRoles.User);
        var recorder = new ReaderCommandRecorder();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
        var service = new PermissionService(new PermissionReader(db), factory.Services.GetRequiredService<IPermissionCatalog>());

        Assert.True(await service.HasPermissionAsync(first, Permissions.Users.Read, Ct));
        Assert.True(await service.HasPermissionAsync(first, Permissions.Roles.Read, Ct));
        Assert.NotEmpty(await service.GetPermissionsAsync(first, Ct));
        Assert.Single(recorder.Commands);

        Assert.False(await service.HasPermissionAsync(second, Permissions.Users.Read, Ct));
        Assert.Equal(2, recorder.Commands.Count);
    }

    /// <summary>Sin caché, un cambio de claims que no pasa por Identity también vale desde el próximo pedido.</summary>
    [Fact]
    public async Task An_out_of_band_claim_change_is_visible_to_the_next_scope()
    {
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        Assert.True(await HasUsersReadAsync(userId));

        await factory.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.RoleClaims.RemoveRange(await db.RoleClaims.Where(claim => claim.RoleId == role.Id).ToListAsync(Ct));
        });

        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Committed_claim_additions_and_removals_are_visible_to_an_independent_api()
    {
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        await using var replica = factory.WithWebHostBuilder(_ => { });
        Assert.Equal([Permissions.Users.Read], await ReadOnAsync(replica.Services, userId));

        await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .UpdateAsync(role.Id, role.Name, null, [Permissions.Users.Read, Permissions.Roles.Read], Ct));
        Assert.Equal(new[] { Permissions.Users.Read, Permissions.Roles.Read }.Order(StringComparer.Ordinal),
            await ReadOnAsync(replica.Services, userId));

        await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .UpdateAsync(role.Id, role.Name, null, [], Ct));
        Assert.Empty(await ReadOnAsync(replica.Services, userId));
    }

    [Fact]
    public async Task Seed_restores_missing_admin_permissions_for_the_next_read()
    {
        var userId = await CreateUserAsync(SystemRoles.Admin);
        var adminId = await factory.ExecuteDbContextAsync(db => db.Roles
            .Where(role => role.Name == SystemRoles.Admin).Select(role => role.Id).SingleAsync(Ct));
        try
        {
            await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
                .UpdateAsync(adminId, SystemRoles.Admin, null, factory.Services.GetRequiredService<IPermissionCatalog>().All.Where(permission => permission != Permissions.Users.Read).ToArray(), Ct));
            Assert.False(await HasUsersReadAsync(userId));

            await factory.Services.SeedDatabaseAsync(Ct);

            Assert.True(await HasUsersReadAsync(userId));
        }
        finally
        {
            await factory.Services.SeedDatabaseAsync(Ct);
        }
    }

    [Fact]
    public async Task Role_reassignment_is_visible_to_the_next_scope()
    {
        var originalRole = await CreateRoleAsync(Permissions.Users.Read);
        var replacementRole = await CreateRoleAsync(Permissions.Roles.Read);
        var userId = await CreateUserAsync(originalRole.Name);

        Assert.Equal([Permissions.Users.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));

        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IUserRepository>().SetRolesAsync(userId, [replacementRole.Name], Ct));

        Assert.Equal([Permissions.Roles.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Reader_returns_only_permission_claims_of_the_user_roles_once_each()
    {
        var role = await CreateRoleAsync(Permissions.Roles.Read);
        var other = await CreateRoleAsync(Permissions.Roles.Read);
        var userId = await CreateUserAsync(role.Name, other.Name);

        // IRoleRepository escribe solo claims de permisos. Uno de otro tipo se agrega directo en el contexto, adentro del
        // límite, y lo baja su guardado final.
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ApplicationDbContext>().RoleClaims.Add(new IdentityRoleClaim<Guid>
            {
                RoleId = role.Id,
                ClaimType = "unrelated",
                ClaimValue = Permissions.Users.Read,
            });

            return Task.CompletedTask;
        });

        var claims = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IPermissionReader>().ListPermissionsForUserAsync(userId, Ct));

        Assert.Equal([Permissions.Roles.Read], claims);
    }

    /// <summary>
    /// Adentro de un límite, la consulta corre en su conexión y su transacción (N33): no pide otra al pool mientras el
    /// límite retiene la suya, así que ve lo que el límite todavía no confirmó. No queda en ningún caché: si el límite se
    /// deshace, el pedido siguiente lee lo confirmado.
    /// </summary>
    [Fact]
    public async Task Inside_a_boundary_permissions_are_read_on_its_connection_and_a_rollback_leaves_nothing_behind()
    {
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        IReadOnlyCollection<string> captured = [];

        await Assert.ThrowsAsync<RolledBackOnPurpose>(() => factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IRoleRepository>().UpdateAsync(
                role.Id, role.Name, null, [Permissions.Users.Read, Permissions.Roles.Read], Ct);
            captured = await services.GetRequiredService<IPermissionService>().GetPermissionsAsync(userId, Ct);

            throw new RolledBackOnPurpose();
        }));

        Assert.Equal(new[] { Permissions.Users.Read, Permissions.Roles.Read }.Order(StringComparer.Ordinal), captured);
        Assert.Equal([Permissions.Users.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
    }

    private Task<bool> HasUsersReadAsync(Guid userId) =>
        WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct));

    /// <summary>Una cuenta nueva con exactamente esos roles, como la dejan el alta y la edición de sus roles.</summary>
    private Task<Guid> CreateUserAsync(params string[] roles) =>
        factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var email = Email.Create("perm-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;
            var user = await users.CreateAsync(email, phone: null, phoneConfirmed: false, displayName: null, "es", Ct);
            await users.SetRolesAsync(user.Id, roles, Ct);

            return user.Id;
        });

    private Task<(Guid Id, string Name)> CreateRoleAsync(string permission) =>
        factory.InTransactionAsync(async services =>
        {
            var name = "permission-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var id = await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, [permission], Ct);

            return (id, name);
        });

    private Task<T> WithPermissionsAsync<T>(Func<IPermissionService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IPermissionService>()));

    /// <summary>Un pedido nuevo de esa Api: su propio scope.</summary>
    private static async Task<IReadOnlyCollection<string>> ReadOnAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetPermissionsAsync(userId, Ct);
    }

    private sealed class RolledBackOnPurpose : Exception;

    /// <summary>Las consultas que salen hacia Postgres.</summary>
    private sealed class ReaderCommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }
}
