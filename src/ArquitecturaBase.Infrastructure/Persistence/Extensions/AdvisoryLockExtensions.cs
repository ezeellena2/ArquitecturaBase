using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class AdvisoryLockExtensions
{
    /// <summary>
    /// Toma un lock de Postgres (<c>pg_advisory_xact_lock</c>) por clave, que dura lo que la transacción: si no hay una,
    /// la abre, y la confirma UnitOfWork al guardar. Las toma ordenadas y sin repetir: dos transacciones que piden las
    /// mismas claves las piden en el mismo orden, y ninguna espera a la otra para siempre. Las claves van como
    /// parámetros: el log de un comando nunca muestra su valor.
    /// </summary>
    public static async Task AcquireAdvisoryLocksAsync(
        this DbContext dbContext,
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        var ordered = keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        foreach (var key in ordered)
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
    }
}
