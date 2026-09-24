using ArquitecturaBase.Application.Common.Exceptions;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Confirma los cambios de un caso de uso. Lo llama UnitOfWorkDecorator, no los handlers.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Guarda todo o nada. Si otro pedido guardó primero una fila con la misma clave única, lanza
    /// <see cref="UniqueConstraintViolationException"/> y deshace la transacción, con sus locks.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
