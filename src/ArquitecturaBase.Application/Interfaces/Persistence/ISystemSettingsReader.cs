using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure sobre
/// HybridCache, igual que los permisos por rol. El caché lo descarta <see cref="Integrations.Caching.ISystemSettingsCache"/>.
/// </summary>
public interface ISystemSettingsReader
{
    /// <summary>
    /// El modo de registro, cacheado. Sin la fila de ajustes no devuelve null ni lanza: devuelve InviteOnly, que es el
    /// modo cerrado.
    /// </summary>
    Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken);
}
