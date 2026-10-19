namespace ArquitecturaBase.Application.Interfaces.Integrations.Identity;

/// <summary>Descarta los permisos en caché de un rol después de confirmar su modificación.</summary>
public interface IPermissionCacheInvalidator
{
    Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
