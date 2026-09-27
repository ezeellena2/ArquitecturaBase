using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class AdvisoryLockExtensions
{
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
}
