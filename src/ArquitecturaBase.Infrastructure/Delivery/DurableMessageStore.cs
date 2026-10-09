using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Delivery;

/// <summary>Enlists an encrypted delivery in the caller's context, without saving or sending.</summary>
internal sealed class DurableMessageStore(ApplicationDbContext dbContext, IDataProtectionProvider protection, TimeProvider timeProvider)
{
    public bool TryEnqueue(string channel, int capacity, string messageType, string payload, DateTime? expiresAtUtc)
    {
        dbContext.RequireTransaction();
        dbContext.AcquireAdvisoryLocks([AdvisoryLockKeys.Delivery(channel)]);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var pending = dbContext.Set<QueuedDelivery>().Count(delivery => delivery.Channel == channel && delivery.ClaimedAtUtc == null && delivery.ExpiresAtUtc > nowUtc);
        pending += dbContext.ChangeTracker.Entries<QueuedDelivery>().Count(entry => entry.State == EntityState.Added && entry.Entity.Channel == channel);
        if (pending >= capacity || expiresAtUtc <= nowUtc) return false;
        var encrypted = protection.CreateProtector("ArquitecturaBase.Delivery.v1", channel, messageType).Protect(payload);
        dbContext.Add(QueuedDelivery.Create(channel, messageType, encrypted, nowUtc, expiresAtUtc ?? nowUtc.AddDays(1)));
        return true;
    }
}
