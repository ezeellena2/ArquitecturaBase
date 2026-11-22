using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// El nombre del sistema es el de Configuración › Apariencia (RC6-B), el mismo de los correos: uno solo para todo lo que ve la
/// persona, leído en cada uso con el caché de los ajustes.
/// </summary>
internal sealed class AppName(ISystemSettingsReader settings) : IAppName
{
    public async Task<string> GetAsync(CancellationToken cancellationToken) =>
        (await settings.FindBrandingAsync(cancellationToken)).AppName;
}
