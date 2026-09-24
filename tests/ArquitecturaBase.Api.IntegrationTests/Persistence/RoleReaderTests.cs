using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class RoleReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleted_users_do_not_count_but_role_details_and_permissions_remain()
    {
        var name = UniqueName();
        var roleId = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateRoleAsync(name, "Solo lectura", [Permissions.Users.Read], Ct));

        var account = await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            var created = await identity.CreateAsync(
                Email.Create(TestEmails.Unique("rolecount")).Value,
                phone: null,
                phoneConfirmed: false,
                displayName: null,
                culture: "es",
                Ct);
            await identity.SetRolesAsync(created.Id, [name], Ct);

            return created;
        });

        var before = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>()
            .FindRoleAsync(roleId, Ct));
        Assert.NotNull(before);
        Assert.Equal(1, before.UserCount);

        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(account.Id, Ct);

            return true;
        });

        var after = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            var detail = await reader.FindRoleAsync(roleId, Ct);
            var listed = await reader.ListRolesAsync(Ct);

            return (detail, listed);
        });
        Assert.NotNull(after.detail);
        Assert.Equal(name, after.detail.Name);
        Assert.Equal("Solo lectura", after.detail.Description);
        Assert.False(after.detail.IsSystemRole);
        Assert.Equal(0, after.detail.UserCount);
        Assert.Equal([Permissions.Users.Read], after.detail.Permissions);
        var listedRole = Assert.Single(after.listed, role => role.Id == roleId);
        Assert.Equal(after.detail.UserCount, listedRole.UserCount);
        Assert.Equal(after.detail.Permissions, listedRole.Permissions);
    }

    [Fact]
    public async Task Name_lookup_uses_identity_normalization_and_can_exclude_the_same_role()
    {
        var name = UniqueName();
        var roleId = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateRoleAsync(name, description: null, [], Ct));

        var result = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            var names = await reader.ListRoleNamesAsync(Ct);
            var matching = await reader.RoleNameExistsAsync(name.ToUpperInvariant(), excludedRoleId: null, Ct);
            var ownExcluded = await reader.RoleNameExistsAsync(name.ToUpperInvariant(), roleId, Ct);
            var otherExcluded = await reader.RoleNameExistsAsync(name.ToUpperInvariant(), Guid.NewGuid(), Ct);

            return (names, matching, ownExcluded, otherExcluded);
        });

        Assert.Contains(name, result.names);
        Assert.True(result.matching);
        Assert.False(result.ownExcluded);
        Assert.True(result.otherExcluded);
    }

    private static string UniqueName() => "rol-" + Guid.NewGuid().ToString("N")[..8];
}
