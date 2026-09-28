using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IRoleService
{
    /// <summary>El catálogo completo de roles, para los selectores.</summary>
    Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken);

    /// <summary>El listado paginado, con búsqueda y orden.</summary>
    Task<Result<PagedResult<RoleResponse>>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken);

    Task<Result<RoleResponse>> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<PermissionGroupResponse>>> GetPermissionsAsync(CancellationToken cancellationToken);

    Task<Result<Guid>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken);
}
