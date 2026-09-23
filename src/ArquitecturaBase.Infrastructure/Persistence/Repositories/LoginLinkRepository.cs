using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginLinkRepository(ApplicationDbContext dbContext) : ILoginLinkRepository
{
    private const string LockKeyPrefix = "login-link:";

    public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([LockKeyPrefix + userId.ToString("N")], cancellationToken);

    // Sin seguimiento a propósito: si la fila quedara en el contexto, la lectura de después del lock devolvería esta
    // misma instancia, con lo que había antes de que otro canje la consumiera.
    public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.LoginLinks
            .AsNoTracking()
            .Where(link => link.TokenHash == tokenHash)
            .Select(link => (Guid?)link.UserId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<LoginLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.LoginLinks.SingleOrDefaultAsync(link => link.TokenHash == tokenHash, cancellationToken);

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

    public async Task<IReadOnlyList<DateTime>> ListIssueTimesSinceAsync(
        Guid userId,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        await dbContext.LoginLinks
            .Where(link => link.UserId == userId && link.CreatedAtUtc > sinceUtc)
            .OrderBy(link => link.CreatedAtUtc)
            .Select(link => link.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(LoginLink loginLink) => dbContext.LoginLinks.Add(loginLink);
}
