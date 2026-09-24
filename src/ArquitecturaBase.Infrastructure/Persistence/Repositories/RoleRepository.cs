using System.Data.Common;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class RoleRepository(RoleManager<ApplicationRole> roleManager, ApplicationDbContext dbContext) : IRoleRepository
{
    public Task<Guid> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var role = new ApplicationRole(name) { Description = description };

            (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
            await SetRolePermissionsAsync(role, permissions);

            return role.Id;
        }, cancellationToken);

    public Task UpdateAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var role = await RequireRoleAsync(roleId, cancellationToken);
            role.Description = description;

            // SetRoleNameAsync escribe el nombre y el normalizado en el store; UpdateAsync es el que guarda.
            (await roleManager.SetRoleNameAsync(role, name)).EnsureSucceeded("rename the role");
            (await roleManager.UpdateAsync(role)).EnsureSucceeded("update the role");

            await SetRolePermissionsAsync(role, permissions);
            return true;
        }, cancellationToken);

    public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            (await roleManager.DeleteAsync(await RequireRoleAsync(roleId, cancellationToken)))
                .EnsureSucceeded("delete the role");
            return true;
        }, cancellationToken);

    private async Task SetRolePermissionsAsync(ApplicationRole role, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(claim => !permissions.Contains(claim.Value, StringComparer.Ordinal)))
        {
            (await roleManager.RemoveClaimAsync(role, claim)).EnsureSucceeded("remove a permission");
        }

        var kept = current.Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !kept.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)))
                .EnsureSucceeded("add a permission");
        }
    }

    private async Task<ApplicationRole> RequireRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roleManager.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("The role does not exist.");

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        // RoleManager usa el mismo DbContext scoped y guarda en cada Create/Update/AddClaim/RemoveClaim.
        // Si el llamador ya abrió una transacción, participa de ella y deja su confirmación al dueño.
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await operation();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (DbException)
            {
                // Si la conexión se cortó, Postgres deshace la transacción; se conserva el error original.
            }

            throw;
        }
    }
}
