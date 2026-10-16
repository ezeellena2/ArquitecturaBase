using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormRecordSource(IFormRecordReader reader, IPermissionService permissions) : IStructuredRecordSource
{
    public async Task<StructuredRecordReadResult> ReadAsync(Guid actorUserId, string culture,
        IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || culture is not ("es" or "en")
            || !await permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.RecordsRead, cancellationToken))
            return new(false, new Dictionary<Guid, StructuredRecordReadReply>());

        if (sourceReceiptIds.Count == 0)
            return new(true, new Dictionary<Guid, StructuredRecordReadReply>());

        var rows = await reader.ListAsync(sourceReceiptIds.Distinct().ToArray(), cancellationToken);
        if (!await permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.RecordsRead, cancellationToken))
            return new(false, new Dictionary<Guid, StructuredRecordReadReply>());
        return new(true, rows.Select(row => (row.SourceReceiptId, Reply: FormRecordPresentationPolicy.Project(row, culture)))
            .ToDictionary(item => item.SourceReceiptId, item => item.Reply));
    }

}
