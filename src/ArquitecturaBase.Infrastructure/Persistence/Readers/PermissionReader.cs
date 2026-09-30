using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class PermissionReader(ApplicationDbContext dbContext) : IPermissionReader
{
    public Task<string?> FindRoleVersionAsync(Guid roleId, CancellationToken cancellationToken) =>
        dbContext.Roles.AsNoTracking()
            .Where(role => role.Id == roleId)
            .Select(role => role.ConcurrencyStamp)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<RolePermissionsRow?> FindRolePermissionsAsync(Guid roleId, string version, CancellationToken cancellationToken) =>
        dbContext.Roles.AsNoTracking()
            .Where(role => role.Id == roleId && role.ConcurrencyStamp == version)
            .Select(role => new RolePermissionsRow(role.ConcurrencyStamp!,
                dbContext.RoleClaims
                    .Where(claim => claim.RoleId == role.Id && claim.ClaimType == Permissions.ClaimType)
                    .Select(claim => claim.ClaimValue!).ToArray()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync(cancellationToken);

    public Task<string[]> ListPermissionsForRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        dbContext.RoleClaims
            .Where(claim => claim.RoleId == roleId && claim.ClaimType == Permissions.ClaimType)
            .Select(claim => claim.ClaimValue!)
            .ToArrayAsync(cancellationToken);
}
