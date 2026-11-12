using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// The origin «Documento» (decision 15) of the responses a reviewer passed from the inbox: <c>channelId</c> <c>documents</c>, the reviewer
/// who passed it and when, and the document with its file's name, read by batch; the responses a person confirmed in a channel keep that
/// channel's origin. The document goes only to whoever can see the inbox («Ver documentos recibidos», asked before and after reading, as
/// the WhatsApp origin hides its conversation); anyone else gets the origin without it.
/// </summary>
internal sealed class BusinessFormDocumentOriginSource(IFormReceivedDocumentReader reader, IPermissionService permissions) : IStructuredRecordOriginSource
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(Guid actorUserId, IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
    {
        if (sourceReceiptIds.Count == 0) return [];
        var canSee = await permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.DocumentsRead, cancellationToken);
        var rows = await reader.ListOriginsAsync(sourceReceiptIds, cancellationToken);
        canSee = canSee && await permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.DocumentsRead, cancellationToken);
        return rows.Select(row => new StructuredRecordOrigin(row.SourceReceiptId, FormReceivedDocumentCapturePolicy.SourceId, row.ReviewerUserId,
            row.ReviewerName, row.ReviewedAtUtc, null, null, nameof(StructuredRecordSenderKind.Manual))
        {
            Document = canSee ? new(row.DocumentId, row.FileName) : null,
        }).ToArray();
    }
}
