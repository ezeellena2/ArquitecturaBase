using ArquitecturaBase.Application.Models.Delivery;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IDeliveryQueueService
{
    /// <summary>N27 and H25: claims only of the message types the caller's version knows.</summary>
    Task<Result<DeliveryMessageResponse?>> ClaimNextAsync(string channel, IReadOnlyCollection<string> messageTypes, CancellationToken cancellationToken);

    /// <summary>
    /// N27 and H25: a delivery the caller could not read goes back to the queue after a wait, for another copy, up to a number of
    /// reads; then it is completed, as before.
    /// </summary>
    Task<Result> LeaveForAnotherCopyAsync(Guid id, CancellationToken cancellationToken);
    Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>H8: a delivery the worker gives back without having called the provider, for another copy to claim it.</summary>
    Task<Result> ReleaseAsync(Guid id, CancellationToken cancellationToken);
}
