using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>A durable, optional delivery channel for actions on already-authorized records.</summary>
public interface IStructuredRecordActionChannel
{
    string ChannelId { get; }

    Task<IReadOnlyList<StructuredRecordActionObservationResponse>> ObserveAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> ReviewAsync(Guid runId, Guid recipientUserId, StructuredRecordActionReview review, CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, long controlRevision, CancellationToken cancellationToken);

    /// <summary>Whether the recipient has an address in this channel, such as a linked contact. A local read: no provider, no reservation.</summary>
    Task<bool> CanReachAsync(Guid recipientUserId, CancellationToken cancellationToken);
}
