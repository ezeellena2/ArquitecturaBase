using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

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
        var roleName = "cache-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var roleId = await factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = new ApplicationRole(roleName);
            await roles.CreateAsync(role);
            await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, Permissions.Users.Read));
            return role.Id;
        });
        var userId = await CreateUserAsync(roleName);
        Assert.True(await HasUsersReadAsync(userId));

        await factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = (await roles.FindByIdAsync(roleId.ToString("D", CultureInfo.InvariantCulture)))!;
            return await roles.RemoveClaimAsync(role, new Claim(Permissions.ClaimType, Permissions.Users.Read));
        });

        Assert.True(await HasUsersReadAsync(userId));

        await WithPermissionsAsync(async service =>
        {
            await service.InvalidateRoleAsync(roleId, Ct);
            return true;
        });

        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Role_reassignment_is_visible_without_invalidating_cached_role_permissions()
    {
        var originalRole = await CreateRoleAsync(Permissions.Users.Read);
        var replacementRole = await CreateRoleAsync(Permissions.Roles.Read);
        var userId = await CreateUserAsync(originalRole.Name);

        Assert.Equal([Permissions.Users.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));

        await factory.ExecuteScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture)))!;
            Assert.True((await users.RemoveFromRoleAsync(user, originalRole.Name)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, replacementRole.Name)).Succeeded);
            return true;
        });

        Assert.Equal([Permissions.Roles.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Reader_returns_only_permission_claims_for_a_role()
    {
        var role = await CreateRoleAsync(Permissions.Roles.Read);
        await factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var storedRole = (await roles.FindByIdAsync(role.Id.ToString("D", CultureInfo.InvariantCulture)))!;
            Assert.True((await roles.AddClaimAsync(storedRole, new Claim("unrelated", Permissions.Users.Read))).Succeeded);
            return true;
        });

        var claims = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IPermissionReader>().GetRolePermissionsAsync(role.Id, Ct));

        Assert.Equal([Permissions.Roles.Read], claims);
    }

    private Task<bool> HasUsersReadAsync(Guid userId) =>
        WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct));

    private Task<Guid> CreateUserAsync(params string[] roles) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var email = "perm-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";
            var user = new ApplicationUser { UserName = email, Email = email };
            await users.CreateAsync(user);
            await users.AddToRolesAsync(user, roles);
            return user.Id;
        });

    private Task<(Guid Id, string Name)> CreateRoleAsync(string permission) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var name = "permission-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var role = new ApplicationRole(name);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            Assert.True((await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission))).Succeeded);
            return (role.Id, name);
        });

    private Task<T> WithPermissionsAsync<T>(Func<IPermissionService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IPermissionService>()));
}
