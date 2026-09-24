using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class RoleRepositoryTransactionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_claim_write_rolls_back_role_and_preceding_claim_writes()
    {
        var oldName = UniqueName();
        var newName = UniqueName();
        var roleId = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateRoleAsync(oldName, "Before", [Permissions.Users.Read], Ct));

        // El nombre, la descripción y dos cambios de claims se autoguardan antes de llegar al valor nulo.
        // El valor nulo provoca un error determinista al construir el último Claim.
        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IRoleRepository>().UpdateAsync(
                roleId, newName, "After", [Permissions.Roles.Read, null!], Ct);
            return true;
        }));

        var persisted = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            return (
                Role: await reader.FindRoleAsync(roleId, Ct),
                NewNameExists: await reader.RoleNameExistsAsync(newName, excludedRoleId: null, Ct));
        });

        Assert.NotNull(persisted.Role);
        Assert.Equal(oldName, persisted.Role.Name);
        Assert.Equal("Before", persisted.Role.Description);
        Assert.Equal([Permissions.Users.Read], persisted.Role.Permissions);
        Assert.False(persisted.NewNameExists);
    }

    [Fact]
    public async Task Service_creates_updates_and_deletes_a_role_through_the_repository()
    {
        var name = UniqueName();
        var renamed = UniqueName();

        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleService>()
            .CreateAsync(new CreateRoleRequest($"  {name}  ", "Before", [Permissions.Users.Read]), Ct));
        Assert.True(created.IsSuccess);

        var updated = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleService>()
            .UpdateAsync(new UpdateRoleRequest(created.Value, renamed, "After", [Permissions.Roles.Read]), Ct));
        Assert.True(updated.IsSuccess);

        var changed = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>()
            .FindRoleAsync(created.Value, Ct));
        Assert.NotNull(changed);
        Assert.Equal(renamed, changed.Name);
        Assert.Equal("After", changed.Description);
        Assert.Equal([Permissions.Roles.Read], changed.Permissions);

        var deleted = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleService>()
            .DeleteAsync(new DeleteRoleRequest(created.Value), Ct));
        Assert.True(deleted.IsSuccess);

        var missing = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>()
            .FindRoleAsync(created.Value, Ct));
        Assert.Null(missing);
    }

    private static string UniqueName() => "rol-" + Guid.NewGuid().ToString("N")[..8];
}
