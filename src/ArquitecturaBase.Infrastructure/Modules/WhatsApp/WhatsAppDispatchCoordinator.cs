using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Caching;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>All senders share a renewable recipient lease and a six-second cooldown across hosts.</summary>
internal sealed class WhatsAppDispatchCoordinator(RedisCache cache, TimeProvider timeProvider) : IWhatsAppDispatchCoordinator
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    public async Task<IWhatsAppDispatchLease> AcquireAsync(PhoneNumber recipient, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient.Value)));
        var key = new RedisCacheKey("whatsapp:dispatch:" + hash);
        var owner = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (await cache.TryAcquireExclusiveLeaseAsync(key, owner, LeaseDuration, cancellationToken))
            {
                return new Lease(cache, key, owner, timeProvider);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
        }

        throw new TimeoutException("The recipient dispatch lease could not be acquired.");
    }

    private sealed class Lease : IWhatsAppDispatchLease
    {
        private readonly RedisCache cache;
        private readonly RedisCacheKey key;
        private readonly string owner;
        private readonly TimeProvider timeProvider;
        private readonly CancellationTokenSource stop = new();
        private readonly CancellationTokenSource lost = new();
        private readonly Task renewal;

        public Lease(RedisCache cache, RedisCacheKey key, string owner, TimeProvider timeProvider)
        {
            this.cache = cache; this.key = key; this.owner = owner; this.timeProvider = timeProvider;
            renewal = RenewAsync();
        }

        public CancellationToken LostOwnership => lost.Token;

        private async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), timeProvider, stop.Token);
                    if (!await cache.RenewExclusiveLeaseAsync(key, owner, LeaseDuration, stop.Token))
                    {
                        await lost.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception)
            {
                await lost.CancelAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await renewal;
            try
            {
                await cache.ReleaseExclusiveLeaseAsync(key, owner, TimeSpan.FromSeconds(6), CancellationToken.None);
            }
            finally
            {
                stop.Dispose();
                lost.Dispose();
            }
        }
    }
}
