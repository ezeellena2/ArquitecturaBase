using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormRecordCaptureGuard(IFormCaptureReceiptReader receipts, IPermissionService permissions, FormRecordAmendmentGuard amendments)
{
    /// <summary>How the person correcting may change responses: a correction is not a new response of theirs.</summary>
    public Task<FormRecordAmendAccess?> FindAmendAccessAsync(CancellationToken ct) => amendments.FindAsync(ct);

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
