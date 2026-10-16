using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>A durable, optional delivery channel for actions on already-authorized records.</summary>
public interface IStructuredRecordActionChannel
{
    string ChannelId { get; }

    Task<StructuredRecordActionStatus> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken);

    Task<StructuredRecordActionStatus> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);

    Task<StructuredRecordActionStatus> RetryAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
}
