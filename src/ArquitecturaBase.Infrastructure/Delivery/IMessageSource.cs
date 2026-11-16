namespace ArquitecturaBase.Infrastructure.Delivery;

internal interface IMessageSource<out T>
{
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// H8: the worker gives back the message it is reading because the host stopped while it waited for its turn, or for the
    /// retry it was going to make anyway, so another copy takes it; after it the source yields nothing more. Never a message
    /// whose last attempt is unknown and not to be retried. One reader at a time; a source without durable claims ignores it.
    /// </summary>
    void GiveBackCurrent() { }

    /// <summary>
    /// N5 and N8: the send of the message being read failed before its body left, for a reason that may pass: it goes back to the
    /// queue after a wait that doubles with each attempt, from <paramref name="firstWait"/> to <paramref name="longestWait"/>, and
    /// the source goes on with the next. <see cref="DeliveryRetry.Expired"/> if the next attempt would come after its expiry (it
    /// expired without going out, and the caller reports it as not sent), or the source keeps no durable claims;
    /// <see cref="DeliveryRetry.NotReturned"/> if putting it back failed, so it stays claimed, as a crash leaves it.
    /// </summary>
    Task<DeliveryRetry> RetryCurrentLaterAsync(TimeSpan firstWait, TimeSpan longestWait, CancellationToken cancellationToken) =>
        Task.FromResult(DeliveryRetry.Expired);

    /// <summary>
    /// N8: the worker settled the message being read and completes it now, with the token it bounds what it records with, so all
    /// of it fits in the host's shutdown. Without it, the source completes it when the worker asks for the next one, with a
    /// bounded token of its own. A source without durable claims ignores it.
    /// </summary>
    Task CompleteCurrentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>N5: what became of a message the worker put back to wait for its next attempt.</summary>
internal enum DeliveryRetry
{
    /// <summary>It waits in the queue for its next attempt.</summary>
    Waits,

    /// <summary>Its next attempt would come after its expiry: it was completed without going out.</summary>
    Expired,

    /// <summary>Putting it back failed (the database, or the bounded token): it stays claimed and is never sent again.</summary>
    NotReturned,
}
