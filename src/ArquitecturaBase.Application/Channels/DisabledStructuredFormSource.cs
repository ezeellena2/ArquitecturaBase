using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledStructuredFormSource : IStructuredFormSource
{
    public Task<Result<StructuredFormSuggestionsResponse>> ListSuggestionsAsync(Guid recipientUserId, string recipientCulture, Guid sourceRecordId, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormSuggestionsResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareSuggestionAsync(StructuredFormSuggestionPreparationRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormSuggestionPreparationResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormClarificationResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormPrefillResponse>(SystemQueryErrors.NotFound)); }
    public Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StructuredFormPreparationResponse([], []));
    }

    public Task LockSelectionAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<StructuredFormSelectionRow?>(null);
    }
    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<StructuredFormSelectionRow?>(null);
    }
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture,
        ListStructuredFormsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PagedResult<StructuredFormSelectionRow>([], request.Page, request.PageSize, 0));
    }
}
