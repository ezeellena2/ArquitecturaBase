using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class TransactionExtensions
{
    /// <summary>
    /// Lanza si no hay una transacción abierta. Un lock de Postgres, advisory o de fila, dura lo que la transacción, y una
    /// escritura con varios autoguardados de Identity solo es atómica dentro de una. Fuera de
    /// IUnitOfWork.ExecuteInTransactionAsync correrían en autocommit. Es un bug de quien llama, no una regla de negocio. La
    /// transacción la abre ExecuteInTransactionAsync, y nadie más.
    /// </summary>
    public static void RequireTransaction(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "This operation needs the transaction of the use case: call it inside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }

    /// <summary>
    /// Lanza si hay una transacción abierta. Lo que corre después del commit del caso de uso (la cookie de la aplicación)
    /// no puede correr adentro de IUnitOfWork.ExecuteInTransactionAsync: si el commit fallara, quedaría hecho algo que
    /// depende de datos que no se guardaron. Es un bug de quien llama, no una regla de negocio.
    /// </summary>
    public static void RequireNoTransaction(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "This operation runs after the use case commits: call it outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
}
