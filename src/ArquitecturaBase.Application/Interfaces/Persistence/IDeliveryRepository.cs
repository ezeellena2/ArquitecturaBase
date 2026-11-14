using ArquitecturaBase.Domain.Delivery;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface IDeliveryRepository
{
    Task LockAsync(string channel, CancellationToken cancellationToken);
    Task LockRetentionAsync(CancellationToken cancellationToken);
    /// <summary>N27 and H25: only of the message types the reader knows, and none a copy left to wait.</summary>
    Task<QueuedDelivery?> GetNextAsync(string channel, IReadOnlyCollection<string> messageTypes, DateTime nowUtc, CancellationToken cancellationToken);
    Task<QueuedDelivery?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken);
    void Remove(QueuedDelivery delivery);
}
