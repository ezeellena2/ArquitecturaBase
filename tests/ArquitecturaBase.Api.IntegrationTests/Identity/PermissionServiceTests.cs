using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles de la cuenta, con los de cada rol cacheados. Los datos se
/// arman como los arma la aplicación: con IUserRepository e IRoleRepository, adentro de un límite
/// (factory.InTransactionAsync).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PermissionServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permissions_are_the_union_of_the_user_roles()
    {
        var userId = await CreateUserAsync(SystemRoles.Admin, SystemRoles.User);

        var permissions = await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct));

        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), permissions);
    }

    [Fact]
    public async Task User_role_has_no_permissions_yet()
    {
        var userId = await CreateUserAsync(SystemRoles.User);

        Assert.Empty(await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct)));
    }

    [Fact]
    public async Task Role_permissions_are_cached_until_the_role_is_invalidated()
    {
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        Assert.True(await HasUsersReadAsync(userId));

        // Se le saca el permiso sin pasar por RoleService, que invalidaría el caché después del commit.
        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IRoleRepository>().UpdateAsync(role.Id, role.Name, null, [], Ct));

        Assert.True(await HasUsersReadAsync(userId));

        await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IPermissionService>().InvalidateRoleAsync(role.Id, Ct));

        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Role_reassignment_is_visible_without_invalidating_cached_role_permissions()
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
    public async Task Reader_returns_only_permission_claims_for_a_role()
    {
        var role = await CreateRoleAsync(Permissions.Roles.Read);

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
            services.GetRequiredService<IPermissionReader>().GetRolePermissionsAsync(role.Id, Ct));

        Assert.Equal([Permissions.Roles.Read], claims);
    }

    /// <summary>
    /// La fábrica del caché lee en su propio scope, con otra conexión: un permiso que quien pregunta todavía no confirmó no
    /// se cachea. Sobre el contexto de quien llama, adentro de un límite, la fábrica vería el cambio sin confirmar y lo
    /// dejaría una hora en el caché aunque el límite se deshiciera.
    /// </summary>
    [Fact]
    public async Task The_cache_factory_reads_on_its_own_connection_and_never_caches_uncommitted_permissions()
    {
        // Un rol nuevo: su clave del caché arranca fría.
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

        Assert.Equal([Permissions.Users.Read], captured);
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

    private sealed class RolledBackOnPurpose : Exception;
}
