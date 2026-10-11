using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Neutral, batched read port for values attached to already-authorized conversation receipts.</summary>
public interface IStructuredRecordSource
{
    Task<StructuredRecordReadResult> ReadAsync(Guid actorUserId, string culture,
        IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken);
}
