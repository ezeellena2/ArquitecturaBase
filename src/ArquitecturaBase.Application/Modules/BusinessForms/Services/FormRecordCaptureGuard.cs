using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormRecordCaptureGuard(IFormCaptureReceiptReader receipts, IPermissionService permissions)
{
    public Task<bool> CanCaptureAsync(Guid actorUserId, CancellationToken ct) =>
        permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.RecordsCreate, ct);

    public async Task<StructuredRecordCaptureResponse?> FindExistingAsync(StructuredRecordCaptureRequest request,
        string fingerprint, CancellationToken ct)
    {
        var prior = await receipts.FindAsync(request.SourceReceiptId, ct);
        if (prior is null || prior.SourceReceiptId != request.SourceReceiptId || prior.DefinitionId != request.DefinitionId
            || prior.VersionId != request.VersionId || prior.Fingerprint != fingerprint) return null;
        return new(prior.State, prior.RecordId, prior.Number, prior.ErrorCode, prior.RejectedFieldId);
    }
}
