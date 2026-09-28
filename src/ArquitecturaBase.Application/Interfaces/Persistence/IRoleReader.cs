using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Roles;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Consultas de roles usadas por la administración y la validación de nombres.</summary>
public interface IRoleReader
{
    Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken);

    /// <summary>El catálogo completo, ordenado por nombre, para los selectores.</summary>
    Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken);

    /// <summary>Una página del listado, con la búsqueda y el orden del pedido.</summary>
    Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken);

    Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken);
}
