using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class PermissionReader(ApplicationDbContext dbContext) : IPermissionReader
{
    public async Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync(cancellationToken);

    public Task<string[]> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        dbContext.RoleClaims
            .Where(claim => claim.RoleId == roleId && claim.ClaimType == Permissions.ClaimType)
            .Select(claim => claim.ClaimValue!)
            .ToArrayAsync(cancellationToken);
}
