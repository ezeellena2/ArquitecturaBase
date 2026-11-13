namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppMessageRetentionRepository
{
    /// <summary>
    /// Borra solo el texto de los mensajes anteriores al umbral UTC que todavía lo conservan.
    /// Devuelve la cantidad de mensajes modificados.
    /// </summary>
    Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
    Task<int> ClearExpiredAssistantContentAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    /// <summary>W3: what was written from what the person shared, of the sends whose shared messages are all past the cutoff.</summary>
    Task<int> ClearExpiredSharedValuesAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken);
}
