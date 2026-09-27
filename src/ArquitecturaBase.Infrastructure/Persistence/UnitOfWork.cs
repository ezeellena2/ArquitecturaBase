using System.Data.Common;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// La única pieza que abre, confirma y deshace transacciones, y la única que guarda en los casos de uso (lo verifica
/// TransactionBoundaryTests; los seeders de arranque guardan por su cuenta, fuera de todo caso de uso). UserManager y
/// RoleManager siguen guardando en cada operación sobre este mismo contexto scoped (D1). Dentro de la transacción, EF
/// le pone un savepoint a cada guardado: un choque con un índice único deshace solo ese guardado y el caso de uso puede
/// seguir, por ejemplo para dejar gastado el código. No usa estrategia de
/// reintentos, porque el trabajo encola correos y mensajes y no se puede volver a correr. Si alguna vez se activa
/// EnableRetryOnFailure, BeginTransactionAsync lanza, y no hay que "arreglarlo" envolviendo el trabajo en la estrategia.
/// </summary>
internal sealed partial class UnitOfWork(ApplicationDbContext dbContext, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CommitPolicy policy,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);
        policy.ThrowIfUndefined();

        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "ExecuteInTransactionAsync does not nest: a use case has a single transaction boundary.");
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction that ExecuteInTransactionAsync did not open is still active in this scope.");
        }

        // READ COMMITTED, el nivel de Postgres. Lo que pone en fila son los locks, y lo que se lee después de un lock tiene
        // que ver lo que el otro acaba de confirmar (ConcurrencyStamp): por eso no se sube el aislamiento.
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _transaction = transaction;

        try
        {
            var result = await work(cancellationToken);

            if (!policy.Commits(result))
            {
                await RollbackAsync(transaction);
                return result;
            }

            // Lo que Identity no guardó por su cuenta (códigos, enlaces, auditorías, contactos, mensajes) se guarda acá.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            // Se deshace ahora y no al descartar el contexto: los locks se sueltan antes de que la excepción llegue a quien
            // llama, y quien reintenta en otro scope (el webhook de WhatsApp) no se queda esperando a este.
            await RollbackAsync(transaction);

            if (UniqueViolations.Translate(exception) is { } unique)
            {
                throw unique;
            }

            throw;
        }
        finally
        {
            _transaction = null;
            await transaction.DisposeAsync();
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            // Sin el token del pedido: aunque se haya cancelado, hay que soltar los locks.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // O se cortó la conexión, o un commit fallido ya cerró la transacción. En los dos casos Postgres la descarta
            // solo, y la excepción que importa es la del caso de uso.
            LogRollbackFailed(logger, exception.GetType().Name);
        }
        finally
        {
            // Lo que está seguido ya no coincide con la base, tampoco lo que Identity marcó como guardado. Así ningún
            // guardado posterior del mismo scope lo revive en autocommit.
            dbContext.ChangeTracker.Clear();
        }
    }

    // Solo el tipo, sin mensaje ni valores: el test de privacidad del webhook corre en Trace.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rolling back a unit of work failed with {ExceptionType}; the transaction had already ended or the database discards it with the connection")]
    private static partial void LogRollbackFailed(ILogger logger, string exceptionType);
}
