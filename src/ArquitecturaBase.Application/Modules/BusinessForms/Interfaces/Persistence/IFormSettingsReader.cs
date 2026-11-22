using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// Configuración › Formularios as it is now (RC4-A): the saved row, or today's values at revision 0 while nobody saved it. Every use reads it
/// again, so a change applies from the next request on, in every copy of the server.
/// </summary>
public interface IFormSettingsReader
{
    Task<FormSettingsResponse> FindAsync(CancellationToken cancellationToken);
}
