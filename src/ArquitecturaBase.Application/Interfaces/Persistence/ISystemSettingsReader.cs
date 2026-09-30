using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Application.Models.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure con el
/// caché compartido. Lo descarta <see cref="Integrations.Caching.ISystemSettingsCache"/> después del commit.
/// </summary>
public interface ISystemSettingsReader
{
    /// <summary>
    /// El modo de registro, cacheado. Sin la fila de ajustes no devuelve null ni lanza: devuelve InviteOnly, que es el
    /// modo cerrado.
    /// </summary>
    Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken);
    Task<SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken);
}
