using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Connection;

/// <summary>
/// RC7-B: las cuentas activas y no borradas (el filtro global) con el correo confirmado y algún rol con el permiso
/// <c>arca.manage</c>, en el orden de su id.
/// </summary>
internal sealed class ArcaNoticeRecipientReader(ApplicationDbContext dbContext) : IArcaNoticeRecipientReader
{
    public async Task<IReadOnlyList<ArcaNoticeRecipientRow>> ListManagersAsync(CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => user.IsActive && user.EmailConfirmed && user.Email != null
                && dbContext.UserRoles.Any(membership => membership.UserId == user.Id
                    && dbContext.RoleClaims.Any(claim => claim.RoleId == membership.RoleId
                        && claim.ClaimType == Permissions.ClaimType && claim.ClaimValue == ArcaPermissions.Manage)))
            .OrderBy(user => user.Id)
            .Select(user => new ArcaNoticeRecipientRow(user.Email!, user.Culture, user.TimeZoneId))
            .ToArrayAsync(cancellationToken);
}
