namespace ArquitecturaBase.Infrastructure.Delivery;

internal interface IMessageSource<out T>
{
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken);
}
