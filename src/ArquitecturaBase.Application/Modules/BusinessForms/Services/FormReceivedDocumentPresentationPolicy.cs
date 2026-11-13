using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox as the app shows it (DOC5-A), in the language of whoever reads: each document with who sent it, its form, its state (whether
/// it is for the team to review here, or until when its person is waited for), its response, its file and up to three data read that help
/// find it (amounts first, then the validated ones and the options); the page adds the forms and senders to filter by, saying when they cut.
/// The detail adds its reading, its proposal with each datum's mark and, for a choice a reviewer can pass (of the document's current form,
/// while it is proposed), the options it can take (its own, or the catalog ones the person who sent it could choose, as a channel would offer
/// them, each with its group) or why its catalog lists none, the extraction that runs, its history and, for a reviewer, the forms with
/// reading it can change to; a list too long is searched. The questions are named as the version each candidate was read for; the private parts are read
/// back from their protection.
/// </summary>
internal sealed class FormReceivedDocumentPresentationPolicy(IFormReceivedDocumentReader reader, FormReceivedDocumentContentPolicy contents,
    FormReceivedDocumentCopyPolicy copies, FormCatalogOptionsPolicy options, FormEntryCatalogPolicy entries)
{
    public const int MaxHighlights = 3;
    /// <summary>The most options a choice lists, as a channel offers them in one list: a longer list gives none and is searched.</summary>
    public const int MaxOptions = 200;

    public async Task<IReadOnlyList<FormReceivedDocumentListItemResponse>> ItemsAsync(IReadOnlyList<FormReceivedDocumentStoredRow> rows, string culture,
        DateTime nowUtc, CancellationToken ct)
    {
        var proposals = rows.ToDictionary(row => row.Id, row => contents.ReadProposal(row.Id, row.ProtectedProposal));
        var versions = await VersionsAsync(proposals.Values.SelectMany(proposal => proposal?.Candidates ?? []).Select(candidate => candidate.VersionId), ct);
        return rows.Select(row => Item(row, proposals[row.Id], versions, culture, nowUtc)).ToArray();
    }

    /// <summary>The options of the «Formulario» and «Mandó» filters, the forms by their name in the reader's language, and whether each list was cut.</summary>
    public static FormReceivedDocumentFacetsResponse Facets(FormReceivedDocumentPageRow page, string culture) => new(
        page.Forms.Select(form => new FormReceivedDocumentFormFacetResponse(form.DefinitionId, form.NameJson is { } nameJson
            ? FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(nameJson), culture) : "", form.Count)).ToArray(),
        page.FormsTruncated,
        page.Senders.Select(sender => new FormReceivedDocumentSenderFacetResponse(sender.UserId, sender.DisplayName, sender.Count)).ToArray(),
        page.SendersTruncated);

    /// <summary>The notice of similar documents (DOC6): each group with its topic as it was noted, how many they are and the newest of them.</summary>
    public static IReadOnlyList<FormReceivedDocumentGroupResponse> Groups(FormReceivedDocumentPageRow page) =>
        page.Groups.Select(group => new FormReceivedDocumentGroupResponse(new(group.TopicId, group.TopicName), group.Count, group.DocumentIds)).ToArray();

    public async Task<FormReceivedDocumentDetailResponse> DetailAsync(FormReceivedDocumentStoredRow row, IReadOnlyList<FormReceivedDocumentEventRow> events,
        string culture, DateTime nowUtc, bool canReview, CancellationToken ct)
    {
        var proposal = contents.ReadProposal(row.Id, row.ProtectedProposal);
        var versions = await VersionsAsync(proposal?.Candidates.Select(candidate => candidate.VersionId) ?? [], ct);
        var reading = contents.ReadReading(row.Id, row.ProtectedReading);
        // The options of a choice only where someone can act on it: a reviewer, for the document's current form while it is proposed, the only
        // one it can be passed to; the other candidates and an unclassified document offer none.
        var canPass = canReview && row.State == FormReceivedDocumentState.Proposed;
        var candidates = new List<FormReceivedDocumentCandidateResponse>();
        foreach (var candidate in proposal?.Candidates.Where(candidate => versions.ContainsKey(candidate.VersionId)) ?? [])
            candidates.Add(await CandidateAsync(row, candidate, versions[candidate.VersionId], culture, canPass && candidate.DefinitionId == row.DefinitionId, ct));
        return new(Item(row, proposal, versions, culture, nowUtc),
            reading is null ? null : new(Camel(reading.Readability.ToString()), reading.Problem is { } problem ? Camel(problem.ToString()) : null,
                reading.Language, reading.Text, reading.Truncated),
            proposal is null ? null : new(proposal.Outcome, proposal.DocumentType, candidates, Repetition(proposal, versions, culture)),
            row.ExtractionRequestId is { } requestId && row.ExtractionDefinitionId is { } definitionId && row.ExtractionRequestedAtUtc is { } requestedAt
                ? new(requestId, definitionId, requestedAt) : null,
            events.OrderBy(item => item.AtUtc).ThenBy(item => item.Id).Select(item => new FormReceivedDocumentEventResponse(item.AtUtc, Camel(item.Kind),
                item.ActorUserId is { } actor ? new(actor, item.ActorName) : null, contents.ReadDetail(row.Id, item.ProtectedDetail))).ToArray(),
            canReview, canReview ? await FormsAsync(culture, ct) : [])
        {
            RowCells = row.RowNumber is null ? null : RowCells(contents.ReadRow(row.Id, row.ProtectedReading)),
        };
    }

    public Task<Stream?> OpenAsync(FormReceivedDocumentStoredRow row, CancellationToken ct) =>
        row.StorageKey is { } key ? copies.OpenAsync(key, ct) : Task.FromResult<Stream?>(null);

    /// <summary>
    /// The options of the sender's list for a catalog choice of the document's current form (one its reading fills) that match what was
    /// typed, as the app searches them for whoever loads: the first 50, and whether more match. Only while it is proposed, as passing it.
    /// </summary>
    public async Task<Result<FormReceivedDocumentOptionsResponse>> SearchOptionsAsync(FormReceivedDocumentStoredRow row, Guid fieldId, string? search,
        string culture, CancellationToken ct)
    {
        if (row.VersionId is not { } versionId || !(await VersionsAsync([versionId], ct)).TryGetValue(versionId, out var version)
            || FormDocumentFormPolicy.ReadQuestions(version.Schema).SingleOrDefault(question => question.FieldId == fieldId) is not
                { Type: "choice", Source.Catalog: not null } question)
            return FormEntryErrors.QuestionNotFound;
        if (row.State != FormReceivedDocumentState.Proposed) return FormReceivedDocumentErrors.StateConflict;
        var page = await entries.ListAsync(row.SenderUserId, question, culture, new ListFormEntryOptionsRequest
        {
            Search = search, Page = 1, PageSize = ListFormEntryOptionsRequest.MaximumPageSize,
        }, ct);
        if (page.IsFailure) return page.Error;
        return new FormReceivedDocumentOptionsResponse(page.Value.Items.Select(option => new FormReceivedDocumentOptionResponse(option.Id, option.Title, null)).ToArray(),
            page.Value.TotalCount > page.Value.Items.Count);
    }

    private static FormReceivedDocumentListItemResponse Item(FormReceivedDocumentStoredRow row, FormDocumentPreparedProposal? proposal,
        IReadOnlyDictionary<Guid, (FormDocumentVersionRow Row, FormVersionSchema Schema)> versions, string culture, DateTime nowUtc)
    {
        var form = row.DefinitionId is { } definitionId && row.DefinitionNameJson is { } nameJson
            ? new FormReceivedDocumentFormResponse(definitionId, FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(nameJson), culture)) : null;
        var record = row.RecordId is { } recordId && row.RecordNumber is { } number && row.Prefix is { } prefix
            ? new FormReceivedDocumentRecordReference(recordId, prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture)) : null;
        var candidate = proposal?.Candidates.FirstOrDefault(value => value.DefinitionId == row.DefinitionId) ?? (proposal?.Candidates is [var first, ..] ? first : null);
        var highlights = candidate is not null && versions.TryGetValue(candidate.VersionId, out var version) ? Highlights(candidate, version.Schema, culture) : [];
        return new(row.Id, row.ReceivedAtUtc, row.ChannelId, new(row.SenderUserId, row.SenderName), form, Camel(row.State.ToString()),
            FormReceivedDocument.IsForReview(row.State, row.NotSendable, row.ReplyExpiresAtUtc, nowUtc),
            FormReceivedDocument.AwaitedUntil(row.State, row.NotSendable, row.ReplyExpiresAtUtc, nowUtc), row.ReasonCode, row.DocumentType, record,
            new(row.FileName, row.MediaType, row.Size, row.Pages, row.StorageKey is not null), highlights, row.DeleteAfterUtc, row.Revision)
        {
            Row = row.RowNumber is { } rowNumber ? new(row.SheetName, rowNumber) : null,
        };
    }

    // Where it repeats, with what a row adds (VC1-A): the questions whose data matched, as the version of its candidate names them in the reader's
    // language, and the earlier row it is equal to.
    private static FormReceivedDocumentRepetitionResponse? Repetition(FormDocumentPreparedProposal proposal,
        IReadOnlyDictionary<Guid, (FormDocumentVersionRow Row, FormVersionSchema Schema)> versions, string culture)
    {
        if (proposal.Repetition is not { } repetition) return null;
        string[]? labels = null;
        if (repetition.FieldIds is { Count: > 0 } fieldIds && proposal.Candidates is [var candidate, ..] && versions.TryGetValue(candidate.VersionId, out var version))
        {
            labels = [.. fieldIds.Select(fieldId => version.Schema.Questions.SingleOrDefault(question => question.FieldId == fieldId)).OfType<CanonicalFormField>()
                .Select(question => FormRecordPresentationPolicy.Text(question.Label, culture)).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.Ordinal)];
            if (labels.Length == 0) labels = null;
        }
        return new(repetition.AtUtc, repetition.RecordId, repetition.RecordNumber, repetition.DocumentId)
        {
            Labels = labels, Row = repetition.RowNumber is { } number ? new(repetition.RowSheet, number) : null,
        };
    }

    // The cells with content of a row as they came, in the order of their columns, each with the header of its column or, when it is empty, its
    // letter (VC1-A). A row is registered only with the text of some cell: one whose reading cannot be read is a failure of its protection,
    // never a row without cells.
    private static FormReceivedDocumentRowCellResponse[] RowCells(FormDocumentRowReading? reading)
    {
        FormReceivedDocumentRowCellResponse[] cells = reading is null ? []
            : [.. reading.Cells.Select((cell, index) => (cell.Text, Index: index)).Where(cell => !string.IsNullOrWhiteSpace(cell.Text))
                .Select(cell => new FormReceivedDocumentRowCellResponse(cell.Index < reading.Headers.Count && reading.Headers[cell.Index] is { } header
                    && !string.IsNullOrWhiteSpace(header) ? header.Trim() : FormAttachmentPreparationPolicy.Column(cell.Index + 1), cell.Text!))];
        return cells.Length > 0 ? cells : throw new InvalidOperationException("The reading of a row of a spreadsheet cannot be read.");
    }

    private static FormReceivedDocumentHighlightResponse[] Highlights(FormDocumentPreparedCandidate candidate, FormVersionSchema schema, string culture) =>
        candidate.Values.Where(value => value.Value is not null && value.Mark != FormDocumentMarks.Invalid)
            .Select(value => (Value: value, Question: schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId)))
            .Where(pair => pair.Question is not null && FormReceivedDocumentReplyPolicy.Display(pair.Value, culture) is not null)
            .OrderBy(pair => pair.Value.Type == "decimal" ? 0 : pair.Question!.Validation is not null ? 1 : pair.Value.Type == "choice" ? 2 : 3)
            .Where(pair => pair.Value.Type == "decimal" || pair.Question!.Validation is not null || pair.Value.Type == "choice")
            .Take(MaxHighlights)
            .Select(pair => new FormReceivedDocumentHighlightResponse(pair.Value.FieldId, FormRecordPresentationPolicy.Text(pair.Question!.Label, culture),
                FormReceivedDocumentReplyPolicy.Display(pair.Value, culture)!)).ToArray();

    private async Task<FormReceivedDocumentCandidateResponse> CandidateAsync(FormReceivedDocumentStoredRow row, FormDocumentPreparedCandidate candidate,
        (FormDocumentVersionRow Row, FormVersionSchema Schema) version, string culture, bool actionable, CancellationToken ct)
    {
        var name = FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.Row.NameJson), culture, version.Row.Culture);
        var pairs = candidate.Values.Select(value => (Value: value, Question: version.Schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId)))
            .Where(pair => pair.Question is not null).ToArray();
        // The catalog options the person who sent it could choose, as a channel would offer them to them, within the maximum per person: only
        // this candidate's catalog choices are prepared, and none where nobody can act.
        var catalogQuestions = actionable
            ? pairs.Select(pair => pair.Question!).Where(question => question.Type == "choice" && question.Source?.Catalog is not null).ToArray() : [];
        var prepared = catalogQuestions.Length > 0
            ? await options.PrepareAsync(row.SenderUserId, culture, version.Schema with { Questions = catalogQuestions }, version.Row.Culture, ct) : null;
        var offers = prepared?.Catalogs ?? [];
        var issues = prepared?.Issues ?? [];
        return new(candidate.DefinitionId, candidate.VersionId, name, pairs
            .Select(pair => new FormReceivedDocumentValueResponse(pair.Value.FieldId, FormRecordPresentationPolicy.Text(pair.Question!.Label, culture),
                pair.Value.Type, pair.Question.Required, pair.Value.Value, FormReceivedDocumentReplyPolicy.Display(pair.Value, culture), pair.Value.Mark,
                pair.Value.ErrorCode, Options(pair.Question, offers, culture, actionable), Unavailable(pair.Question, offers, issues, actionable))).ToArray());
    }

    // What a choice can take where someone can act: its own options, or the offered ones of its catalog as the channel shows them (none when it
    // cannot be offered or lists more than MaxOptions), each with the text of its group in a grouped catalog; an empty list where nobody can
    // act, and nothing for another type.
    private static FormReceivedDocumentOptionResponse[]? Options(CanonicalFormField question, IReadOnlyList<StructuredFormCatalogOffer> offers, string culture,
        bool actionable)
    {
        if (question.Type != "choice") return null;
        if (!actionable) return [];
        if (question.Source?.Catalog is null)
            return (question.Options ?? []).Select(option => new FormReceivedDocumentOptionResponse(option.ChoiceId, FormRecordPresentationPolicy.Text(option.Label, culture),
                null)).ToArray();
        if (offers.FirstOrDefault(offer => offer.FieldId == question.FieldId) is not { Options.Count: <= MaxOptions } offer) return [];
        return offer.Options.Select(option => new FormReceivedDocumentOptionResponse(option.Id, option.Shown?.Title ?? option.Text,
            offer.Grouping?.Groups.FirstOrDefault(group => group.Value == option.Group)?.Text)).ToArray();
    }

    // Why a catalog choice someone can act on lists no options: more than MaxOptions for the sender (then it is searched), or a datum of the
    // sender its filters need, named in the reader's language; null when its options are all there, or nobody can act.
    private static FormReceivedDocumentOptionsUnavailableResponse? Unavailable(CanonicalFormField question, IReadOnlyList<StructuredFormCatalogOffer> offers,
        IReadOnlyList<StructuredFormPreparationIssue> issues, bool actionable)
    {
        if (!actionable || question.Type != "choice" || question.Source?.Catalog is null) return null;
        if (offers.FirstOrDefault(offer => offer.FieldId == question.FieldId) is { } offer)
            return offer.Options.Count > MaxOptions ? new(FormReceivedDocumentOptionsUnavailableResponse.TooMany) : null;
        return issues.FirstOrDefault(issue => issue.FieldId == question.FieldId) switch
        {
            { } issue when issue.Code == FormRecordErrors.CatalogTooLarge.Code => new(FormReceivedDocumentOptionsUnavailableResponse.TooMany),
            { MissingData: { } datum } issue when issue.Code == FormRecordErrors.RecipientDataMissing.Code =>
                new(FormReceivedDocumentOptionsUnavailableResponse.MissingData, datum),
            _ => null,
        };
    }

    // The active forms with reading on a reviewer can change a document to (on in the query, the first 100: more is not a real case), by name in
    // their language.
    private async Task<FormReceivedDocumentFormResponse[]> FormsAsync(string culture, CancellationToken ct) =>
        (await reader.ListReadingFormNamesAsync(ct))
            .Select(form => new FormReceivedDocumentFormResponse(form.DefinitionId,
                FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(form.NameJson), culture, form.Culture)))
            .OrderBy(form => form.Name, StringComparer.Create(CultureInfo.GetCultureInfo(culture), true)).ThenBy(form => form.DefinitionId).ToArray();

    private async Task<IReadOnlyDictionary<Guid, (FormDocumentVersionRow Row, FormVersionSchema Schema)>> VersionsAsync(IEnumerable<Guid> versionIds, CancellationToken ct)
    {
        var ids = versionIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, (FormDocumentVersionRow, FormVersionSchema)>();
        return (await reader.ListVersionsAsync(ids, ct)).ToDictionary(row => row.VersionId, row => (row, FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson)));
    }

    private static string Camel(string value) => JsonNamingPolicy.CamelCase.ConvertName(value);
}
