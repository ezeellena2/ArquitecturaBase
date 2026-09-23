using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginCodeRepository(ApplicationDbContext dbContext) : ILoginCodeRepository
{
    private const string LockKeyPrefix = "login-code:";

    public async Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken)
    {
        // El lock de Postgres dura lo que la transacción: se abre acá y la confirma UnitOfWork al guardar.
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        // La clave es solo el destino, sin el propósito: todo lo que se pide para ese correo o ese número va en la
        // misma fila. Para un correo es la misma clave de antes.
        var key = LockKeyPrefix + destination.Value;
        await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    // Entre códigos del mismo instante gana el que todavía se puede usar. Sin ese desempate, dos códigos que
    // comparten CreatedAtUtc dejan el resultado en manos de la base, y si devuelve uno ya consumido, el código
    // recién emitido se rechaza con "ya se usó". El Id no sirve para desempatar: es un Guid v7, que ordena entre
    // milisegundos distintos pero es aleatorio dentro del mismo. Si la única fila es la consumida, se devuelve
    // igual: reusar un código tiene que seguir diciendo que ya se usó.
    public Task<LoginCode?> GetLatestAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        CancellationToken cancellationToken) =>
        dbContext.LoginCodes
            .Where(code => code.Destination == destination.Value && code.Purpose == purpose && code.InvalidatedAtUtc == null)
            .OrderByDescending(code => code.CreatedAtUtc)
            .ThenBy(code => code.ConsumedAtUtc != null)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LoginCode>> ListActiveAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LoginCodes
            .Where(code => code.Destination == destination.Value
                && code.Purpose == purpose
                && code.ConsumedAtUtc == null
                && code.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);

        // La regla de "activo" vive en el dominio; acá solo se acota la consulta.
        return candidates.Where(code => code.IsActive(nowUtc)).ToList();
    }

    // Sin filtrar por propósito: los límites son por destino.
    public async Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(
        LoginCodeDestination destination,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        await dbContext.LoginCodes
            .Where(code => code.Destination == destination.Value && code.CreatedAtUtc > sinceUtc)
            .OrderBy(code => code.CreatedAtUtc)
            .Select(code => code.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(LoginCode loginCode) => dbContext.LoginCodes.Add(loginCode);
}
