using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppSettingsRepository
{
    /// <summary>Serializes the saves of Configuración › WhatsApp, also the first one, when there is no row to lock yet.</summary>
    Task LockAsync(CancellationToken cancellationToken);

    /// <summary>The settings as saved, tracked; null before the first save.</summary>
    Task<WhatsAppSettings?> GetAsync(CancellationToken cancellationToken);

    void Add(WhatsAppSettings settings);
}
