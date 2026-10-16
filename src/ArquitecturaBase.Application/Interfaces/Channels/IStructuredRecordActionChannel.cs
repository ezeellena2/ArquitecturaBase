using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>A durable, optional delivery channel for actions on already-authorized records.</summary>
public interface IStructuredRecordActionChannel
{
    string ChannelId { get; }

    Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);

    Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
}
