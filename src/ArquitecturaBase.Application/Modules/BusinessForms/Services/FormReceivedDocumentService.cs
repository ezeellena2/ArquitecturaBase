using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Documentos recibidos» (DOC5-A, decisions 14 to 16): the reads ask for «Ver documentos recibidos» before and after reading, without a
/// unit of work, and searching the sender's options of a choice asks for «Revisar documentos» the same way; discarding and reopening ask
/// for «Revisar documentos», with the expected revision and 409, in one unit of work under the document's lock, and record the step in its
/// history.
/// </summary>
internal sealed class FormReceivedDocumentService(FormReceivedDocumentAccessGuard guard, IFormReceivedDocumentReader reader,
    IFormReceivedDocumentRepository repository, FormReceivedDocumentPresentationPolicy presentation, FormReceivedDocumentRecorder recorder,
    IUnitOfWork unitOfWork, TimeProvider clock, IRequestValidator validator) : IFormReceivedDocumentService
{
    public async Task<Result<FormReceivedDocumentsPageResponse>> ListAsync(ListFormReceivedDocumentsRequest request, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsRead, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var digits = FormReceivedDocumentSearchPolicy.Search(request.Search);
        if (!string.IsNullOrWhiteSpace(request.Search) && digits is null) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "search");
        var now = clock.GetUtcNow().UtcDateTime;
        var page = await reader.FindPageAsync(request, digits, now, cancellationToken);
        var items = await presentation.ItemsAsync(page.Items, Culture(), now, cancellationToken);
        if (!await guard.HasAsync(actor.Value.Id, BusinessFormsPermissions.DocumentsRead, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return new FormReceivedDocumentsPageResponse(items, request.Page, request.PageSize, page.TotalCount, page.Counts,
            FormReceivedDocumentPresentationPolicy.Facets(page, Culture()), FormReceivedDocumentPresentationPolicy.Groups(page));
    }

    public async Task<Result<FormReceivedDocumentCountsResponse>> CountAsync(CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsRead, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var counts = await reader.CountAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new FormReceivedDocumentCountsResponse(counts.ForReview, counts.Unclassified, counts.ForReview + counts.Unclassified);
    }

    public async Task<Result<FormReceivedDocumentDetailResponse>> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsRead, cancellationToken);
        return actor.IsFailure ? actor.Error : await DetailAsync(actor.Value, documentId, cancellationToken);
    }

    // A reviewer searches the sender's list of a catalog choice that lists too many options, asking for «Revisar documentos» before and after.
    public async Task<Result<FormReceivedDocumentOptionsResponse>> SearchOptionsAsync(Guid documentId, SearchFormReceivedDocumentOptionsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.FieldId is not { } fieldId || fieldId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "fieldId");
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is { Length: > PagedRequest.MaxSearchLength } || search is not null && !StorableText.IsStorable(search))
            return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "search");
        if (await reader.FindAsync(documentId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.NotFound;
        var found = await presentation.SearchOptionsAsync(row, fieldId, search, Culture(), cancellationToken);
        if (!await guard.HasAsync(actor.Value.Id, BusinessFormsPermissions.DocumentsReview, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return found;
    }

    public async Task<Result<FormReceivedDocumentFileResponse>> OpenAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsRead, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await reader.FindAsync(documentId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.NotFound;
        if (row.StorageKey is null) return FormReceivedDocumentErrors.FileNotStored;
        var stream = await presentation.OpenAsync(row, cancellationToken);
        if (stream is null) return FormReceivedDocumentErrors.FileUnavailable;
        if (await guard.HasAsync(actor.Value.Id, BusinessFormsPermissions.DocumentsRead, cancellationToken))
            return new FormReceivedDocumentFileResponse(stream, row.MediaType, row.FileName);
        await stream.DisposeAsync();
        return FormDefinitionErrors.Forbidden;
    }

    public Task<Result<FormReceivedDocumentDetailResponse>> DiscardAsync(Guid documentId, ReviewFormReceivedDocumentRequest request, CancellationToken cancellationToken) =>
        ReviewAsync(documentId, request, FormReceivedDocumentEventKind.Discarded, (document, revision, now) => document.Discard(revision, now), cancellationToken);

    public Task<Result<FormReceivedDocumentDetailResponse>> ReopenAsync(Guid documentId, ReviewFormReceivedDocumentRequest request, CancellationToken cancellationToken) =>
        ReviewAsync(documentId, request, FormReceivedDocumentEventKind.Reopened, (document, revision, now) => document.Reopen(revision, now), cancellationToken);

    private async Task<Result<FormReceivedDocumentDetailResponse>> ReviewAsync(Guid documentId, ReviewFormReceivedDocumentRequest request,
        FormReceivedDocumentEventKind kind, Func<FormReceivedDocument, long, DateTime, Result> change, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.ExpectedRevision is not (>= 1 and <= 9007199254740991) || documentId == Guid.Empty)
            return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "expectedRevision");
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await repository.LockAsync(documentId, ct);
            if (await repository.GetAsync(documentId, ct) is not { } document) return FormReceivedDocumentErrors.NotFound;
            if (!await guard.HasAsync(actor.Value.Id, BusinessFormsPermissions.DocumentsReview, ct)) return FormDefinitionErrors.Forbidden;
            var changed = change(document, request.ExpectedRevision.Value, clock.GetUtcNow().UtcDateTime);
            return changed.IsFailure ? changed : recorder.Record(document.Id, kind, actor.Value.Id);
        }, CommitPolicy.OnSuccess, cancellationToken);
        return saved.IsFailure ? saved.Error : await DetailAsync(actor.Value, documentId, cancellationToken);
    }

    private async Task<Result<FormReceivedDocumentDetailResponse>> DetailAsync(UserAccount actor, Guid documentId, CancellationToken ct)
    {
        if (await reader.FindAsync(documentId, ct) is not { } row) return FormReceivedDocumentErrors.NotFound;
        var events = await reader.ListEventsAsync(documentId, ct);
        var canReview = await guard.HasAsync(actor.Id, BusinessFormsPermissions.DocumentsReview, ct);
        var detail = await presentation.DetailAsync(row, events, Culture(), clock.GetUtcNow().UtcDateTime, canReview, ct);
        return await guard.HasAsync(actor.Id, BusinessFormsPermissions.DocumentsRead, ct) || canReview ? detail : FormDefinitionErrors.Forbidden;
    }

    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
