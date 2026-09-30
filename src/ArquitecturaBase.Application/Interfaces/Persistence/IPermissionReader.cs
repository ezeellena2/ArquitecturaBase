using ArquitecturaBase.Application.Models.Identity;

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

    /// <summary>The current version of a role, or null if it does not exist.</summary>
    Task<string?> FindRoleVersionAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>A snapshot from that exact version, or null if the role changed or no longer exists.</summary>
    Task<RolePermissionsRow?> FindRolePermissionsAsync(Guid roleId, string version, CancellationToken cancellationToken);
}
