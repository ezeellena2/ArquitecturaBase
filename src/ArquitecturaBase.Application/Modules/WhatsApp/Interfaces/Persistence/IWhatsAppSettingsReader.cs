using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppSettingsReader
{
    /// <summary>
    /// Configuración › WhatsApp as saved, or the values of before the screen (revision 0) before the first save. Read on every use, never
    /// kept: a save changes the next send, code or clearing, in any copy of the Api.
    /// </summary>
    Task<WhatsAppSettingsResponse> FindAsync(CancellationToken cancellationToken);
}
