using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginCodeRepository(ApplicationDbContext dbContext) : ILoginCodeRepository
{
    private const string LockKeyPrefix = "login-code:";

    public async Task LockEmailAsync(Email email, CancellationToken cancellationToken)
    {
        // El lock de Postgres dura lo que la transacción: se abre acá y la confirma UnitOfWork al guardar.
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        var key = LockKeyPrefix + email.Value;
        await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    // Entre códigos del mismo instante gana el que todavía se puede usar. Sin ese desempate, dos códigos que
    // comparten CreatedAtUtc dejan el resultado en manos de la base, y si devuelve uno ya consumido, el código
    // recién emitido se rechaza con "ya se usó". El Id no sirve para desempatar: es un Guid v7, que ordena entre
    // milisegundos distintos pero es aleatorio dentro del mismo. Si la única fila es la consumida, se devuelve
    // igual: reusar un código tiene que seguir diciendo que ya se usó.
    public Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken) =>
        dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.InvalidatedAtUtc == null)
            .OrderByDescending(code => code.CreatedAtUtc)
            .ThenBy(code => code.ConsumedAtUtc != null)
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
