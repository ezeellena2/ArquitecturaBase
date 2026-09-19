namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Permisos efectivos de un usuario: la suma de los permisos de sus roles.</summary>
public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken);

    /// <summary>Descarta los permisos en caché de los usuarios de ese rol. Se llama cuando cambia el rol.</summary>
    Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
