using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

internal sealed class UnavailableStructuredRecordSource : IStructuredRecordSource
{
    public Task<StructuredRecordReadResult> ReadAsync(Guid actorUserId, string culture,
        IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StructuredRecordReadResult(false, new Dictionary<Guid, StructuredRecordReadReply>()));
    }
}
