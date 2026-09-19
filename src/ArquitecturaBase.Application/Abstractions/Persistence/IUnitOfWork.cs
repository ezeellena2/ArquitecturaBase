namespace ArquitecturaBase.Application.Abstractions.Persistence;

/// <summary>Confirma los cambios de un caso de uso. Lo llama UnitOfWorkDecorator, no los handlers.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
