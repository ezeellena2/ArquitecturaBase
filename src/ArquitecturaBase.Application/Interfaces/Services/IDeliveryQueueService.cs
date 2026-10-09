using ArquitecturaBase.Application.Models.Delivery;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IDeliveryQueueService
{
    Task<Result<DeliveryMessageResponse?>> ClaimNextAsync(string channel, CancellationToken cancellationToken);
    Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken);
}
