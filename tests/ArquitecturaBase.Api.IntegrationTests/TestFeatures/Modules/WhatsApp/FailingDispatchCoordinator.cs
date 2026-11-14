using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using StackExchange.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// H3: the turn to write to a person while Redis is down. The first <c>failures</c> acquisitions fail as the Redis client does,
/// and the next ones take an immediate turn without Redis; it counts the attempts.
/// </summary>
internal sealed class FailingDispatchCoordinator(int failures, bool failRelease = false) : IWhatsAppDispatchCoordinator
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public Task<IWhatsAppDispatchLease> AcquireAsync(PhoneNumber recipient, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _attempts) <= failures)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is down (test).");
        return Task.FromResult<IWhatsAppDispatchLease>(new Lease(failRelease));
    }

    // With failRelease, Redis goes down between taking the turn and releasing it.
    private sealed class Lease(bool failRelease) : IWhatsAppDispatchLease
    {
        public CancellationToken LostOwnership => CancellationToken.None;

        public ValueTask DisposeAsync() => failRelease
            ? ValueTask.FromException(new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis went down (test)."))
            : ValueTask.CompletedTask;
    }
}
