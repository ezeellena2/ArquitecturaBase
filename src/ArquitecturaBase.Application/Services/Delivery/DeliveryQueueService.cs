using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Delivery;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Services.Delivery;

internal sealed class DeliveryQueueService(IDeliveryRepository deliveries, IUnitOfWork unitOfWork, TimeProvider timeProvider) : IDeliveryQueueService
{
    /// <summary>N27 and H25: how long a delivery a copy could not read waits for another, and how many reads it gets.</summary>
    internal static readonly TimeSpan ReadRetryWait = TimeSpan.FromSeconds(30);
    internal const int MaxReadFailures = 10;

    public Task<Result<DeliveryMessageResponse?>> ClaimNextAsync(string channel, IReadOnlyCollection<string> messageTypes, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            await deliveries.LockRetentionAsync(ct);
            await deliveries.DeleteExpiredAsync(nowUtc, ct);
            await deliveries.LockAsync(channel, ct);
            var next = await deliveries.GetNextAsync(channel, messageTypes, nowUtc, ct);
            if (next is null) return Result.Success<DeliveryMessageResponse?>(null);
            next.Claim(nowUtc);
            return Result.Success<DeliveryMessageResponse?>(new DeliveryMessageResponse(next.Id, next.MessageType, next.ProtectedPayload)
                { ExpiresAtUtc = next.ExpiresAtUtc });
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> ReleaseAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var delivery = await deliveries.GetAsync(id, ct);
            if (delivery?.ClaimedAtUtc is not null) delivery.Release();
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> LeaveForAnotherCopyAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var delivery = await deliveries.GetAsync(id, ct);
            if (delivery?.ClaimedAtUtc is null) return Result.Success();
            if (!delivery.LeaveForAnotherCopy(timeProvider.GetUtcNow().UtcDateTime + ReadRetryWait, MaxReadFailures)) deliveries.Remove(delivery);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var delivery = await deliveries.GetAsync(id, ct);
            if (delivery is not null) deliveries.Remove(delivery);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
}
