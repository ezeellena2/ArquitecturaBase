using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IRoleService
{
    Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<PermissionGroup>>> GetPermissionsAsync(CancellationToken cancellationToken);
}
