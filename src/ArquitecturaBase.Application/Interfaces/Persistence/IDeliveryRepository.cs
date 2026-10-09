using ArquitecturaBase.Domain.Delivery;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface IDeliveryRepository
{
    Task LockAsync(string channel, CancellationToken cancellationToken);
    Task LockRetentionAsync(CancellationToken cancellationToken);
    Task<QueuedDelivery?> GetNextAsync(string channel, DateTime nowUtc, CancellationToken cancellationToken);
    Task<QueuedDelivery?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken);
    void Remove(QueuedDelivery delivery);
}
