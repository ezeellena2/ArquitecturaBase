using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormSource(IFormSelectionReader reader, IPermissionService permissions, IFormDraftRepository drafts, FormCatalogPreparationPolicy preparation, FormAssistantPrefillPolicy prefill, FormAssistantClarificationPolicy clarification) : IStructuredFormSource
{
    public Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken cancellationToken) =>
        clarification.ValidateAsync(request, cancellationToken);
    public Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken cancellationToken) =>
        prefill.ValidateAsync(request, cancellationToken);
    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken cancellationToken)
    {
        if (await FindAsync(recipientUserId, recipientCulture, definitionId, versionId, cancellationToken) is null)
            return new([], [new(Guid.Empty, "", FormRecordErrors.AuthorizationChanged.Code)]);
        return await preparation.PrepareAsync(recipientUserId, recipientCulture, definitionId, versionId, forSending, cancellationToken);
    }
    public Task LockSelectionAsync(CancellationToken cancellationToken) => drafts.LockDefinitionsAsync(cancellationToken);
    public async Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipientUserId == Guid.Empty || recipientCulture is not ("es" or "en")
            || !await permissions.HasPermissionAsync(recipientUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken)) return null;
        return await reader.FindAsync(definitionId, versionId, cancellationToken);
    }
    public async Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipientUserId == Guid.Empty || recipientCulture is not ("es" or "en")
            || !await permissions.HasPermissionAsync(recipientUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken)) return null;
        return await reader.FindPublishedAsync(definitionId, versionId, cancellationToken);
    }
    public async Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture,
        ListStructuredFormsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipientUserId == Guid.Empty || recipientCulture is not ("es" or "en")
            || !await permissions.HasPermissionAsync(recipientUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken))
            return new([], request.Page, request.PageSize, 0);
        return await reader.ListAsync(recipientCulture, request, cancellationToken);
    }
}
