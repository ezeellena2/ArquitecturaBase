using System.Runtime.CompilerServices;
using ArquitecturaBase.Infrastructure.Delivery;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal sealed class IdleMessageSource<T> : IMessageSource<T>
{
    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}
