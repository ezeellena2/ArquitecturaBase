using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras de roles con RoleManager, que guarda el rol y cada claim por separado sobre el contexto compartido. No abre
/// transacción: exige la del caso de uso. Sin ella, un rol podría quedar con parte de sus permisos.
/// </summary>
internal sealed class RoleRepository(RoleManager<ApplicationRole> roleManager, ApplicationDbContext dbContext) : IRoleRepository
{
    public async Task<Guid> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var role = new ApplicationRole(name) { Description = description };
        (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        await SetRolePermissionsAsync(role, permissions);

        return role.Id;
    }

    public async Task UpdateAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var role = await RequireRoleAsync(roleId, cancellationToken);
        role.Description = description;

        // SetRoleNameAsync escribe el nombre y el normalizado en el store; UpdateAsync es el que guarda.
        (await roleManager.SetRoleNameAsync(role, name)).EnsureSucceeded("rename the role");
        (await roleManager.UpdateAsync(role)).EnsureSucceeded("update the role");

        await SetRolePermissionsAsync(role, permissions);
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await roleManager.DeleteAsync(await RequireRoleAsync(roleId, cancellationToken)))
            .EnsureSucceeded("delete the role");
    }

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
}
