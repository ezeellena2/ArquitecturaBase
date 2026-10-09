using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Delivery;

internal abstract partial class DurableMessageSource<T>(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger logger) : IMessageSource<T>
{
    protected abstract string Channel { get; }
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
                await CompleteAsync(delivery.Id, cancellationToken);
                continue;
            }
            yield return message;
            await CompleteAsync(delivery.Id, cancellationToken);
        }
    }

    private async Task CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A completed external send must not be repeated if recording completion fails.
            LogReadFailure(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The durable delivery queue operation failed with {ExceptionType}")]
    private static partial void LogReadFailure(ILogger logger, string exceptionType);
}
