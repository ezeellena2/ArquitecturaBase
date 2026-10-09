using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Delivery;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Services.Delivery;

internal sealed class DeliveryQueueService(IDeliveryRepository deliveries, IUnitOfWork unitOfWork, TimeProvider timeProvider) : IDeliveryQueueService
{
    public Task<Result<DeliveryMessageResponse?>> ClaimNextAsync(string channel, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            await deliveries.LockRetentionAsync(ct);
            await deliveries.DeleteExpiredAsync(nowUtc, ct);
            await deliveries.LockAsync(channel, ct);
            var next = await deliveries.GetNextAsync(channel, nowUtc, ct);
            if (next is null) return Result.Success<DeliveryMessageResponse?>(null);
            next.Claim(nowUtc);
            return Result.Success<DeliveryMessageResponse?>(new DeliveryMessageResponse(next.Id, next.MessageType, next.ProtectedPayload));
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var delivery = await deliveries.GetAsync(id, ct);
            if (delivery is not null) deliveries.Remove(delivery);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
}
