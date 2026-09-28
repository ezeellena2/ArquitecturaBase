using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
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
        var roleId = await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .CreateAsync(oldName, "Before", [Permissions.Users.Read], Ct));

        // El nombre, la descripción y dos cambios de claims se autoguardan antes de llegar al valor nulo.
        // El valor nulo provoca un error determinista al construir el último Claim.
        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async ct =>
                {
                    await services.GetRequiredService<IRoleRepository>().UpdateAsync(
                        roleId, newName, "After", [Permissions.Roles.Read, null!], ct);

                    return Result.Success();
                },
                CommitPolicy.OnSuccess,
                Ct)));

        var persisted = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            return (
                Role: await reader.FindByIdAsync(roleId, Ct),
                NewNameExists: await reader.ExistsByNameAsync(newName, excludedRoleId: null, Ct));
        });

        Assert.NotNull(persisted.Role);
        Assert.Equal(oldName, persisted.Role.Name);
        Assert.Equal("Before", persisted.Role.Description);
        Assert.Equal([Permissions.Users.Read], persisted.Role.Permissions);
        Assert.False(persisted.NewNameExists);
    }

    /// <summary>
    /// Cada escritura de roles exige la transacción del caso de uso y, sin ella, lanza antes de tocar nada. El mensaje
    /// distingue esa guarda de los otros InvalidOperationException del repositorio (un rol que no existe, un rechazo de
    /// Identity).
    /// </summary>
    [Theory]
    [InlineData(nameof(IRoleRepository.CreateAsync))]
    [InlineData(nameof(IRoleRepository.UpdateAsync))]
    [InlineData(nameof(IRoleRepository.DeleteAsync))]
    public async Task Role_writes_outside_a_transaction_throw_and_change_nothing(string write)
    {
        var name = UniqueName();
        var other = UniqueName();
        var roleId = await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .CreateAsync(name, "Before", [Permissions.Users.Read], Ct));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await (write switch
            {
                nameof(IRoleRepository.CreateAsync) =>
                    (Task)roles.CreateAsync(other, null, [Permissions.Users.Read], Ct),
                nameof(IRoleRepository.UpdateAsync) =>
                    roles.UpdateAsync(roleId, other, "After", [Permissions.Roles.Read], Ct),
                nameof(IRoleRepository.DeleteAsync) => roles.DeleteAsync(roleId, Ct),
                _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown role write."),
            });
        }));

        Assert.Contains(nameof(IUnitOfWork.ExecuteInTransactionAsync), error.Message, StringComparison.Ordinal);
        var persisted = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            return (
                Role: await reader.FindByIdAsync(roleId, Ct),
                OtherExists: await reader.ExistsByNameAsync(other, excludedRoleId: null, Ct));
        });
        Assert.NotNull(persisted.Role);
        Assert.Equal(name, persisted.Role.Name);
        Assert.Equal("Before", persisted.Role.Description);
        Assert.Equal([Permissions.Users.Read], persisted.Role.Permissions);
        Assert.False(persisted.OtherExists);
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
            .FindByIdAsync(created.Value, Ct));
        Assert.NotNull(changed);
        Assert.Equal(renamed, changed.Name);
        Assert.Equal("After", changed.Description);
        Assert.Equal([Permissions.Roles.Read], changed.Permissions);

        var deleted = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleService>()
            .DeleteAsync(new DeleteRoleRequest(created.Value), Ct));
        Assert.True(deleted.IsSuccess);

        var missing = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>()
            .FindByIdAsync(created.Value, Ct));
        Assert.Null(missing);
    }

    private static string UniqueName() => "rol-" + Guid.NewGuid().ToString("N")[..8];
}
