namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de roles sobre Identity. RoleManager guarda el rol y cada claim por separado. Cada operación exige la
/// transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync): sin ella lanza InvalidOperationException, porque
/// un rol podría quedar con parte de sus permisos.
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
