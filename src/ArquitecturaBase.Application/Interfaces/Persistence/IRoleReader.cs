using ArquitecturaBase.Application.Models.Roles.ReadModels;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Consultas de roles usadas por la administración y la validación de nombres.</summary>
public interface IRoleReader
{
    Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken);

    Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken);
}
