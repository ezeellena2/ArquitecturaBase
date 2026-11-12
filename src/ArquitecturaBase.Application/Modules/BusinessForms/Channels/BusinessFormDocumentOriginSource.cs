using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// The origin «Documento» (decision 15) of the responses a reviewer passed from the inbox: <c>channelId</c> <c>documents</c>, the reviewer
/// who passed it and when, read by batch; the responses a person confirmed in a channel keep that channel's origin.
/// </summary>
internal sealed class BusinessFormDocumentOriginSource(IFormReceivedDocumentReader reader) : IStructuredRecordOriginSource
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(Guid actorUserId, IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        sourceReceiptIds.Count == 0 ? [] : (await reader.ListOriginsAsync(sourceReceiptIds, cancellationToken))
            .Select(row => new StructuredRecordOrigin(row.SourceReceiptId, FormReceivedDocumentCapturePolicy.SourceId, row.ReviewerUserId, row.ReviewerName,
                row.ReviewedAtUtc, null, null, nameof(StructuredRecordSenderKind.Manual))).ToArray();
}
