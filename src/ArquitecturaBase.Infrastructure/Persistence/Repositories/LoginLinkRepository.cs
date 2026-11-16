using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginLinkRepository(ApplicationDbContext dbContext) : ILoginLinkRepository
{
    public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.LoginLink(userId)], cancellationToken);

    public async Task<IReadOnlyList<LoginLink>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LoginLinks
            .Where(link => link.UserId == userId
                && link.ConsumedAtUtc == null
                && link.InvalidatedAtUtc == null
                && link.ExpiresAtUtc > nowUtc)
            .ToListAsync(cancellationToken);

        // La regla de "activo" vive en el dominio; acá solo se acota la consulta.
        return candidates.Where(link => link.IsActive(nowUtc)).ToList();
    }

    public async Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.LoginLinks
            .Where(link => link.UserId == userId && link.ConsumedAtUtc == null && link.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);
}
