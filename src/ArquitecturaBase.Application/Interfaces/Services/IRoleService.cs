using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IRoleService
{
    Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<PermissionGroup>>> GetPermissionsAsync(CancellationToken cancellationToken);

    Task<Result<Guid>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken);
}
