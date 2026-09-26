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
}
