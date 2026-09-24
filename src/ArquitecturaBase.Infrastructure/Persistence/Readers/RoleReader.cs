using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class RoleReader(ApplicationDbContext dbContext, RoleManager<ApplicationRole> roleManager) : IRoleReader
{
    public async Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        await dbContext.Roles
            .AsNoTracking()
            .Select(role => role.Name!)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        await LoadRolesAsync(roleId: null, cancellationToken);

    /// <summary>
    /// La misma forma para el listado y para el detalle. UserCount cuenta sobre dbContext.Users, que arrastra el
    /// filtro global: un usuario borrado no mantiene vivo a un rol.
    /// </summary>
    private async Task<List<RoleListItem>> LoadRolesAsync(Guid? roleId, CancellationToken cancellationToken)
    {
        var query = dbContext.Roles.AsNoTracking();

        if (roleId is { } id)
        {
            query = query.Where(role => role.Id == id);
        }

        var rows = await query
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                Name = role.Name!,
                role.Description,
                UserCount = dbContext.Users.Count(user =>
                    dbContext.UserRoles.Any(userRole => userRole.RoleId == role.Id && userRole.UserId == user.Id)),
                Permissions = dbContext.RoleClaims
                    .Where(claim => claim.RoleId == role.Id && claim.ClaimType == Permissions.ClaimType)
                    .Select(claim => claim.ClaimValue!)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new RoleListItem(
                row.Id,
                row.Name,
                row.Description,
                SystemRoles.All.Contains(row.Name, StringComparer.Ordinal),
                row.UserCount,
                [.. row.Permissions.Order(StringComparer.Ordinal)])),
        ];
    }

    public async Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await LoadRolesAsync(roleId, cancellationToken)).FirstOrDefault();

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken)
    {
        var normalized = roleManager.NormalizeKey(name);

        return dbContext.Roles.AnyAsync(
            role => role.NormalizedName == normalized && (excludedRoleId == null || role.Id != excludedRoleId),
            cancellationToken);
    }
}
