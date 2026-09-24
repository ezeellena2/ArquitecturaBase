namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de roles sobre Identity. Cada operación compuesta usa una sola transacción: RoleManager guarda el rol y
/// cada claim por separado, antes de que el caso de uso pueda llamar a IUnitOfWork.
/// </summary>
public interface IRoleRepository
{
    Task<Guid> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken);
}
