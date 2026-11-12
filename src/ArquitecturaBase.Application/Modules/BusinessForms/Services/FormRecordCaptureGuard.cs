using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormRecordCaptureGuard(IFormCaptureReceiptReader receipts, IPermissionService permissions, FormRecordAmendmentGuard amendments,
    IFormRecordIssuanceReader issuances, IUserReader users)
{
    /// <summary>
    /// A received document passed to its form (task 7): whoever sent it is still active, and the reviewer, active too, has «Revisar
    /// documentos», which replaces the sender's permission to load responses for this path only.
    /// </summary>
    public async Task<bool> CanCaptureDocumentAsync(Guid senderUserId, Guid reviewerUserId, CancellationToken ct) =>
        senderUserId != Guid.Empty && reviewerUserId != Guid.Empty
        && await users.FindByIdAsync(senderUserId, ct) is { IsActive: true } && await users.FindByIdAsync(reviewerUserId, ct) is { IsActive: true }
        && await permissions.HasPermissionAsync(reviewerUserId, BusinessFormsPermissions.DocumentsReview, ct);

    /// <summary>How the person correcting may change responses: a correction is not a new response of theirs.</summary>
    public Task<FormRecordAmendAccess?> FindAmendAccessAsync(CancellationToken ct) => amendments.FindAsync(ct);

    /// <summary>
    /// Whether the response has a live issuance, as <see cref="IFormRecordIssuanceReader"/> defines it: issued or being issued, it is
    /// voided and loaded again, never corrected (<see cref="FormRecordErrors.IssuedNotAmendable"/>).
    /// </summary>
    public Task<bool> HasLiveIssuanceAsync(Guid recordId, CancellationToken ct) => issuances.ExistsLiveAsync(recordId, ct);

    public Task<bool> CanCaptureAsync(Guid actorUserId, CancellationToken ct) =>
        permissions.HasPermissionAsync(actorUserId, BusinessFormsPermissions.RecordsCreate, ct);

    public async Task<StructuredRecordCaptureResponse?> FindExistingAsync(StructuredRecordCaptureRequest request,
        string fingerprint, CancellationToken ct)
    {
        var prior = await receipts.FindAsync(request.SourceReceiptId, ct);
        if (prior is null || prior.SourceReceiptId != request.SourceReceiptId || prior.DefinitionId != request.DefinitionId
            || prior.VersionId != request.VersionId || prior.Fingerprint != fingerprint) return null;
        return new(prior.State, prior.RecordId, prior.Number, prior.ErrorCode, prior.RejectedFieldId, prior.RejectedRow);
    }
}
