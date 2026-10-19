using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class CapturingStructuredFormSource : IStructuredFormSource
{
    public Task<Result<StructuredFormSuggestionsResponse>> ListSuggestionsAsync(Guid recipientUserId, string recipientCulture, Guid sourceRecordId, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormSuggestionsResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareSuggestionAsync(StructuredFormSuggestionPreparationRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormSuggestionPreparationResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormClarificationResponse>(SystemQueryErrors.NotFound)); }
    public Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result.Failure<StructuredFormPrefillResponse>(SystemQueryErrors.NotFound)); }
    public StructuredFormPreparationResponse Preparation { get; set; } = new([], []);
    public StructuredFormSelectionRow? Form { get; set; }
    public List<StructuredFormSelectionRow> PublishedForms { get; } = [];

    public Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Preparation with { Culture = Preparation.Culture ?? recipientCulture });
    }

    public Task LockSelectionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId,
        Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult(Form is { } form && form.DefinitionId == definitionId && form.VersionId == versionId ? form : null);

    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId,
        Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult(PublishedForms.FirstOrDefault(form => form.DefinitionId == definitionId && form.VersionId == versionId)
            ?? (Form is { } form && form.DefinitionId == definitionId && form.VersionId == versionId ? form : null));

    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture,
        ListStructuredFormsRequest request, CancellationToken cancellationToken)
    {
        var items = Form is { } form ? new[] { form } : [];
        var page = items.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray();
        return Task.FromResult(new PagedResult<StructuredFormSelectionRow>(page, request.Page, request.PageSize, items.Length));
    }
}
