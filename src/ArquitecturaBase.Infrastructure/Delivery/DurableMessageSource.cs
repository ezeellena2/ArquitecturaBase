using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Delivery;

internal abstract partial class DurableMessageSource<T>(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger logger) : IMessageSource<T>
{
    private bool _givenBack;

    protected abstract string Channel { get; }

    public void GiveBackCurrent() => _givenBack = true;
    protected abstract T Deserialize(string messageType, string payload);

    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ArquitecturaBase.Application.Models.Delivery.DeliveryMessageResponse? delivery;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(Channel, cancellationToken);
                delivery = result.Value;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReadFailure(logger, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }
            if (delivery is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                continue;
            }
            // Claim is committed before decryption and sending. A crash leaves it claimed, never automatically resent.
            T message;
            try
            {
                var payload = protection.CreateProtector("ArquitecturaBase.Delivery.v1", Channel, delivery.MessageType).Unprotect(delivery.ProtectedPayload);
                message = Deserialize(delivery.MessageType, payload);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReadFailure(logger, exception.GetType().Name);
                await CompleteAsync(delivery.Id);
                continue;
            }
            // H8: a message the worker gave back (waiting for its turn, or for the retry the queue was going to make anyway) goes
            // back to the channel for another copy, even if the worker stopped reading. One it abandoned without giving it back
            // stays claimed and is never resent: it may have reached the provider.
            _givenBack = false;
            var finished = false;
            try
            {
                yield return message;
                finished = true;
            }
            finally
            {
                if (_givenBack) await ReleaseAsync(delivery.Id);
            }
            if (_givenBack) yield break;
            if (finished) await CompleteAsync(delivery.Id);
        }
    }

    // Completing or giving back confirms what the worker did, with a bounded token of its own instead of the shutdown's; it is
    // the system clock, as the workers' own tokens, so it adds no timers to the fake clocks of the tests.
    private async Task CompleteAsync(Guid id)
    {
        try
        {
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(id, bounded.Token);
        }
        catch (Exception exception)
        {
            // A completed external send must not be repeated if recording completion fails.
            LogReadFailure(logger, exception.GetType().Name);
        }
    }

    private async Task ReleaseAsync(Guid id)
    {
        try
        {
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().ReleaseAsync(id, bounded.Token);
        }
        catch (Exception exception)
        {
            // Not given back, it stays claimed and expires, as a crash leaves it.
            LogReadFailure(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The durable delivery queue operation failed with {ExceptionType}")]
    private static partial void LogReadFailure(ILogger logger, string exceptionType);
}
