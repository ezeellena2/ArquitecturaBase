using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Roles;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Consultas de roles usadas por la administración y la validación de nombres.</summary>
public interface IRoleReader
{
    Task<RoleRow?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken);

    /// <summary>El catálogo completo, ordenado por nombre, para los selectores.</summary>
    Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken);

    /// <summary>Una página del listado, con la búsqueda y el orden del pedido.</summary>
    Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken);

    Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Los roles de una cuenta, con sus permisos, en una sola consulta. La regla de no dar lo que no se tiene (N1) compara
    /// por id lo que una cuenta tenía con lo que le quedó: un rol renombrado entre dos lecturas no cambia de permisos.
    /// </summary>
    Task<IReadOnlyCollection<RoleRow>> ListRolesForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken);
}
