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
}
