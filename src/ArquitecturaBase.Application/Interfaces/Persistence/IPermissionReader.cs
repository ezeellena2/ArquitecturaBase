namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas especializadas para resolver permisos efectivos. Los roles de una cuenta se consultan en cada petición;
/// los claims de cada rol se pueden cachear e invalidar por separado.
/// </summary>
public interface IPermissionReader
{
    Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task<string[]> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);
}
