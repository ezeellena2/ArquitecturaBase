using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class AdvisoryLockExtensions
{
    // The existing queue port is synchronous; this only waits for PostgreSQL, never for an external sender.
    public static void AcquireAdvisoryLocks(this DbContext dbContext, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(keys);
        dbContext.RequireTransaction();
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            dbContext.Database.ExecuteSql($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
        }
    }
    /// <summary>
    /// El mismo lock que <see cref="AcquireAdvisoryLocksAsync"/>, como sentencia de una migración, que corre en su propia
    /// transacción y lo suelta al confirmar. Recibe el número que da hashtextextended(clave, 0): una migración no nombra la
    /// clave de un módulo, y la prueba de esa migración compara el número con la clave.
    /// </summary>
    public static string TransactionLockStatement(long key) =>
        string.Create(CultureInfo.InvariantCulture, $"SELECT pg_advisory_xact_lock({key});");

    /// <summary>
    /// Toma un pg_advisory_xact_lock por clave (ver AdvisoryLockKeys). Dura lo que la transacción del caso de uso, que
    /// abre IUnitOfWork.ExecuteInTransactionAsync: este método no abre una propia, la exige. Las claves se toman ordenadas
    /// y sin repetir, así dos transacciones que piden las mismas claves en una llamada las piden en el mismo orden. Van
    /// como parámetro, así el log de un comando nunca muestra su valor.
    /// </summary>
    public static async Task AcquireAdvisoryLocksAsync(
        this DbContext dbContext,
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Antes de mirar las claves: un llamador sin transacción es un bug aunque esta vez no pida ninguna.
        dbContext.RequireTransaction();

        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
    }

    /// <summary>
    /// Como <see cref="AcquireAdvisoryLocksAsync"/> con una sola clave, pero sin esperar: pg_try_advisory_xact_lock. True si
    /// lo tomó, y entonces dura lo que la transacción; false si otra transacción lo tiene. Para un trabajo de fondo que,
    /// si encuentra a otra copia en lo mismo, no tiene nada que hacer (N16).
    /// </summary>
    public static async Task<bool> TryAcquireAdvisoryLockAsync(
        this DbContext dbContext,
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        dbContext.RequireTransaction();

        var acquired = await dbContext.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({key}, 0)) AS \"Value\"")
            .ToListAsync(cancellationToken);

        return acquired.Single();
    }
}
