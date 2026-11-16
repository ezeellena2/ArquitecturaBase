using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Una sola sentencia sobre el contexto del pedido: <c>SELECT DISTINCT</c> de los claims de permiso de los roles que la
/// cuenta tiene en <c>AspNetUserRoles</c>, que entra por su clave primaria (UserId, RoleId) y llega a los claims por el
/// índice de RoleId. Si el contexto está en un límite, la consulta va en su transacción.
/// </summary>
internal sealed class PermissionReader(ApplicationDbContext dbContext) : IPermissionReader
{
    public async Task<IReadOnlyCollection<string>> ListPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.RoleClaims
            .AsNoTracking()
            .Where(claim => claim.ClaimType == Permissions.ClaimType
                && dbContext.UserRoles.Any(userRole => userRole.UserId == userId && userRole.RoleId == claim.RoleId))
            .Select(claim => claim.ClaimValue!)
            .Distinct()
            .ToListAsync(cancellationToken);
}
