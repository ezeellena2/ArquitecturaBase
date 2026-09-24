using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure sobre
/// HybridCache, igual que los permisos por rol.
/// </summary>
public interface ISystemSettingsReader
{
    Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken);

    /// <summary>Descarta lo cacheado: lo que se cambia desde el panel vale al instante, sin reiniciar nada.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
