using ArquitecturaBase.Application.Abstractions.Persistence;

namespace ArquitecturaBase.Infrastructure.Persistence;

internal sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changes = await dbContext.SaveChangesAsync(cancellationToken);

        // Si un repositorio abrió una transacción (por ejemplo, para un lock), se confirma con el guardado.
        if (dbContext.Database.CurrentTransaction is { } transaction)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return changes;
    }
}
