using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Application.Models.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure con el
/// caché compartido. Lo descarta <see cref="Integrations.Caching.ISystemSettingsCache"/> después del commit. Si Redis
/// no responde, lee la base sin cachear (N9): el ingreso sigue.
/// </summary>
public interface ISystemSettingsReader
{
    /// <summary>
    /// El modo de registro, cacheado. Sin la fila de ajustes no devuelve null ni lanza: devuelve InviteOnly, que es el
    /// modo cerrado.
    /// </summary>
    Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken);
    Task<SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// RC6-B: el nombre de la aplicación, el del remitente y el logo de los correos, cacheados como la presentación: los lee
    /// cada correo, cada invitación y cada PDF. Sin la fila de ajustes, los iniciales.
    /// </summary>
    Task<SystemBrandingResponse> FindBrandingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// RC1-A: los plazos de los datos del ingreso, de la base y sin caché: la retención los lee en cada corrida. Sin la fila
    /// de ajustes, los iniciales.
    /// </summary>
    Task<IdentityRetentionResponse> FindIdentityRetentionAsync(CancellationToken cancellationToken);
}
