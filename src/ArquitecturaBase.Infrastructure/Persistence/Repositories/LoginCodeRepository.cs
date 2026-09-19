using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginCodeRepository(ApplicationDbContext dbContext) : ILoginCodeRepository
{
    public Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken) =>
        dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.InvalidatedAtUtc == null)
            .OrderByDescending(code => code.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.ConsumedAtUtc == null && code.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);

        // La regla de "activo" vive en el dominio; acá solo se acota la consulta.
        return candidates.Where(code => code.IsActive(nowUtc)).ToList();
    }

    public async Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(
        Email email,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        await dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.CreatedAtUtc > sinceUtc)
            .OrderBy(code => code.CreatedAtUtc)
            .Select(code => code.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(LoginCode loginCode) => dbContext.LoginCodes.Add(loginCode);
}
