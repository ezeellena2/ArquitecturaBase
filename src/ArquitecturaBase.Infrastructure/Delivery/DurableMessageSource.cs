using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Delivery;

internal abstract partial class DurableMessageSource<T>(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger logger) : IMessageSource<T>
{
    private bool _givenBack;
    private Guid? _current;
    private bool _settled;

    /// <summary>What completing or giving back a message may take, with a token of its own and not the shutdown's.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    protected abstract string Channel { get; }

    /// <summary>N27 and H25: the message types this version reads; it claims no other.</summary>
    protected abstract IReadOnlyCollection<string> MessageTypes { get; }

    public void GiveBackCurrent() => _givenBack = true;

    public async Task<DeliveryRetry> RetryCurrentLaterAsync(TimeSpan firstWait, TimeSpan longestWait, CancellationToken cancellationToken)
    {
        if (_current is not { } id || _settled) return DeliveryRetry.NotReturned;
        // Settled either way: it waits in the queue, it expired and was completed, or it stays claimed, as a crash leaves it.
        _settled = true;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().RetryLaterAsync(id, firstWait, longestWait, cancellationToken);
            if (result.IsFailure) return DeliveryRetry.NotReturned;
            return result.Value ? DeliveryRetry.Waits : DeliveryRetry.Expired;
        }
        catch (Exception exception)
        {
            LogReadFailure(logger, exception.GetType().Name);
            return DeliveryRetry.NotReturned;
        }
    }

    public async Task CompleteCurrentAsync(CancellationToken cancellationToken)
    {
        if (_current is not { } id || _settled) return;
        _settled = true;
        await CompleteAsync(id, cancellationToken);
    }

    protected abstract T Deserialize(string messageType, string payload);

    /// <summary>H3: the message with the expiry its row keeps, for a channel whose messages carry it.</summary>
    protected virtual T WithExpiry(T message, DateTime? expiresAtUtc) => message;

    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ArquitecturaBase.Application.Models.Delivery.DeliveryMessageResponse? delivery;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(Channel, MessageTypes, cancellationToken);
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
                message = WithExpiry(Deserialize(delivery.MessageType, payload), delivery.ExpiresAtUtc);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // N27 and H25: what this copy cannot read (its key ring does not open it, or its version does not know its content)
                // is left for another copy, with a wait and a cap, instead of being deleted.
                LogReadFailure(logger, exception.GetType().Name);
                await LeaveForAnotherCopyAsync(delivery.Id);
                continue;
            }
            // H8: a message the worker gave back (waiting for its turn, or for the retry the queue was going to make anyway) goes
            // back to the channel for another copy, even if the worker stopped reading. One it abandoned without giving it back
            // stays claimed and is never resent: it may have reached the provider.
            _givenBack = false;
            _settled = false;
            _current = delivery.Id;
            var finished = false;
            try
            {
                yield return message;
                finished = true;
            }
            finally
            {
                _current = null;
                if (_givenBack && !_settled) await ReleaseAsync(delivery.Id);
            }
            if (_givenBack) yield break;
            // N5: one the worker put back to wait for its next attempt, or completed itself, is not completed again.
            if (finished && !_settled)
            {
                using var bounded = new CancellationTokenSource(SettleTimeout);
                await CompleteAsync(delivery.Id, bounded.Token);
            }
        }
    }

    // Completing or giving back confirms what the worker did, with a bounded token of its own instead of the shutdown's; it is
    // the system clock, as the workers' own tokens, so it adds no timers to the fake clocks of the tests.
    private async Task CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(id, cancellationToken);
        }
        catch (Exception exception)
        {
            // A completed external send must not be repeated if recording completion fails.
            LogReadFailure(logger, exception.GetType().Name);
        }
    }

    private async Task LeaveForAnotherCopyAsync(Guid id)
    {
        try
        {
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().LeaveForAnotherCopyAsync(id, bounded.Token);
        }
        catch (Exception exception)
        {
            // Not left, it stays claimed and expires, as a crash leaves it.
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
