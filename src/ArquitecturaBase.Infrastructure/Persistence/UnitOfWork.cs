using System.Data.Common;
using ArquitecturaBase.Application.Abstractions.Persistence;

namespace ArquitecturaBase.Infrastructure.Persistence;

internal sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        int changes;

        try
        {
            changes = await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Si un repositorio abrió una transacción (por ejemplo, para un lock), se deshace acá y no cuando se
            // descarte el contexto: sus locks se sueltan ya, y quien quiera reintentar en otro scope no se queda
            // esperando a este.
            await RollbackAsync();

            if (UniqueViolations.Translate(exception) is { } unique)
            {
                throw unique;
            }

            throw;
        }

        // Si un repositorio abrió una transacción (por ejemplo, para un lock), se confirma con el guardado.
        if (dbContext.Database.CurrentTransaction is { } transaction)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        return changes;
    }

    private async Task RollbackAsync()
    {
        if (dbContext.Database.CurrentTransaction is not { } transaction)
        {
            return;
        }

        try
        {
            // Sin el token del pedido: si se canceló, igual hay que soltar los locks.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // La conexión ya se cortó: la base deshace la transacción sola, y la excepción que importa es la del guardado.
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
}
