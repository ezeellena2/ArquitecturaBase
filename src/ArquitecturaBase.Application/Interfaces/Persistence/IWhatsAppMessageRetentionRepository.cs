namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface IWhatsAppMessageRetentionRepository
{
    /// <summary>
    /// Borra solo el texto de los mensajes anteriores al umbral UTC que todavía lo conservan.
    /// Devuelve la cantidad de mensajes modificados.
    /// </summary>
    Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
