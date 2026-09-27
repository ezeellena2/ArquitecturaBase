namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas especializadas para resolver permisos efectivos. Los roles de una cuenta se consultan en cada petición;
/// los claims de cada rol se pueden cachear e invalidar por separado.
/// </summary>
public interface IPermissionReader
{
    /// <summary>Los Id de los roles de la cuenta. Se leen en cada petición: no se cachean.</summary>
    Task<IReadOnlyList<Guid>> ListRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Los permisos del rol: sus claims de permiso, sin los de otro tipo. PermissionService los cachea por rol.
    /// </summary>
    Task<string[]> ListPermissionsForRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
