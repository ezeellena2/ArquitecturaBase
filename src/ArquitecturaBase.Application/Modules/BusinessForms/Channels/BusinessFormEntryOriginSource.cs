using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>The origin of responses loaded in the app: «app», who loaded them and when. Receipts from a channel are not its own.</summary>
internal sealed class BusinessFormEntryOriginSource(IFormEntryReader reader) : IStructuredRecordOriginSource
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(Guid actorUserId, IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        sourceReceiptIds.Count == 0 ? [] : (await reader.ListOriginsAsync(sourceReceiptIds, cancellationToken))
            .Select(row => new StructuredRecordOrigin(row.SourceReceiptId, FormEntryChannel.Id, row.ActorUserId, row.ActorName, row.SubmittedAtUtc,
                null, null, nameof(StructuredRecordSenderKind.Manual))).ToArray();
}
