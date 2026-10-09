using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class DeliveryRepository(ApplicationDbContext dbContext) : IDeliveryRepository
{
    public Task LockAsync(string channel, CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.Delivery(channel)], cancellationToken);
    public Task LockRetentionAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.DeliveryRetention], cancellationToken);
    public Task<QueuedDelivery?> GetNextAsync(string channel, DateTime nowUtc, CancellationToken cancellationToken) =>
        dbContext.Set<QueuedDelivery>().Where(delivery => delivery.Channel == channel && delivery.ClaimedAtUtc == null && delivery.ExpiresAtUtc > nowUtc)
            .OrderBy(delivery => delivery.CreatedAtUtc).ThenBy(delivery => delivery.Id).FirstOrDefaultAsync(cancellationToken);
    public Task<QueuedDelivery?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<QueuedDelivery>().SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken);
    public async Task DeleteExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expired = await dbContext.Set<QueuedDelivery>().Where(delivery => delivery.ExpiresAtUtc <= nowUtc).ToListAsync(cancellationToken);
        dbContext.RemoveRange(expired);
    }
    public void Remove(QueuedDelivery delivery) => dbContext.Remove(delivery);
}
