using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox as the app shows it (DOC5-A), in the language of whoever reads: each document with who sent it, its form, its state (and
/// whether it is for the team to review here), its response, its file and up to three data read that help find it (amounts first, then
/// the validated ones and the options); the detail adds its reading, its proposal with each datum's mark, the extraction that runs and its
/// history. The questions are named as the version each candidate was read for; the private parts are read back from their protection.
/// </summary>
internal sealed class FormReceivedDocumentPresentationPolicy(IFormReceivedDocumentReader reader, FormReceivedDocumentContentPolicy contents,
    FormReceivedDocumentCopyPolicy copies)
{
    public const int MaxHighlights = 3;

    public async Task<IReadOnlyList<FormReceivedDocumentListItemResponse>> ItemsAsync(IReadOnlyList<FormReceivedDocumentStoredRow> rows, string culture,
        DateTime nowUtc, CancellationToken ct)
    {
        var proposals = rows.ToDictionary(row => row.Id, row => contents.ReadProposal(row.Id, row.ProtectedProposal));
        var versions = await VersionsAsync(proposals.Values.SelectMany(proposal => proposal?.Candidates ?? []).Select(candidate => candidate.VersionId), ct);
        return rows.Select(row => Item(row, proposals[row.Id], versions, culture, nowUtc)).ToArray();
    }

    public async Task<FormReceivedDocumentDetailResponse> DetailAsync(FormReceivedDocumentStoredRow row, IReadOnlyList<FormReceivedDocumentEventRow> events,
        string culture, DateTime nowUtc, bool canReview, CancellationToken ct)
    {
        var proposal = contents.ReadProposal(row.Id, row.ProtectedProposal);
        var versions = await VersionsAsync(proposal?.Candidates.Select(candidate => candidate.VersionId) ?? [], ct);
        var reading = contents.ReadReading(row.Id, row.ProtectedReading);
        return new(Item(row, proposal, versions, culture, nowUtc),
            reading is null ? null : new(Camel(reading.Readability.ToString()), reading.Problem is { } problem ? Camel(problem.ToString()) : null,
                reading.Language, reading.Text, reading.Truncated),
            proposal is null ? null : new(proposal.Outcome, proposal.DocumentType,
                proposal.Candidates.Where(candidate => versions.ContainsKey(candidate.VersionId)).Select(candidate => Candidate(candidate, versions[candidate.VersionId], culture)).ToArray(),
                proposal.Repetition),
            row.ExtractionRequestId is { } requestId && row.ExtractionDefinitionId is { } definitionId && row.ExtractionRequestedAtUtc is { } requestedAt
                ? new(requestId, definitionId, requestedAt) : null,
            events.OrderBy(item => item.AtUtc).ThenBy(item => item.Id).Select(item => new FormReceivedDocumentEventResponse(item.AtUtc, Camel(item.Kind),
                item.ActorUserId is { } actor ? new(actor, item.ActorName) : null, contents.ReadDetail(row.Id, item.ProtectedDetail))).ToArray(),
            canReview);
    }

    public Task<Stream?> OpenAsync(FormReceivedDocumentStoredRow row, CancellationToken ct) => copies.OpenAsync(row.StorageKey, ct);

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
            FormReceivedDocument.IsForReview(row.State, row.NotSendable, row.ReplyExpiresAtUtc, nowUtc), row.ReasonCode, row.DocumentType, record,
            new(row.FileName, row.MediaType, row.Size, row.Pages), highlights, row.DeleteAfterUtc, row.Revision);
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

    private static FormReceivedDocumentCandidateResponse Candidate(FormDocumentPreparedCandidate candidate,
        (FormDocumentVersionRow Row, FormVersionSchema Schema) version, string culture)
    {
        var name = FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.Row.NameJson), culture, version.Row.Culture);
        return new(candidate.DefinitionId, candidate.VersionId, name, candidate.Values
            .Select(value => (Value: value, Question: version.Schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId)))
            .Where(pair => pair.Question is not null)
            .Select(pair => new FormReceivedDocumentValueResponse(pair.Value.FieldId, FormRecordPresentationPolicy.Text(pair.Question!.Label, culture),
                pair.Value.Type, pair.Question.Required, pair.Value.Value, FormReceivedDocumentReplyPolicy.Display(pair.Value, culture), pair.Value.Mark,
                pair.Value.ErrorCode)).ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, (FormDocumentVersionRow Row, FormVersionSchema Schema)>> VersionsAsync(IEnumerable<Guid> versionIds, CancellationToken ct)
    {
        var ids = versionIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, (FormDocumentVersionRow, FormVersionSchema)>();
        return (await reader.ListVersionsAsync(ids, ct)).ToDictionary(row => row.VersionId, row => (row, FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson)));
    }

    private static string Camel(string value) => JsonNamingPolicy.CamelCase.ConvertName(value);
}
