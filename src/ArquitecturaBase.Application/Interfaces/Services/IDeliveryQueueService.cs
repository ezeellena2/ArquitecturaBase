using ArquitecturaBase.Application.Models.Delivery;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IDeliveryQueueService
{
    Task<Result<DeliveryMessageResponse?>> ClaimNextAsync(string channel, CancellationToken cancellationToken);
    Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>H8: a delivery the worker gives back without having called the provider, for another copy to claim it.</summary>
    Task<Result> ReleaseAsync(Guid id, CancellationToken cancellationToken);
}
