namespace ArquitecturaBase.Application.Interfaces.Integrations.Identity;

/// <summary>Permisos efectivos de un usuario: la suma de los permisos de sus roles.</summary>
public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken);
}
