using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What formularios decides about a spreadsheet whose rows a channel registers (VC1-A, decision 12), before the unit of work. Over the day's
/// quota, one entry unclassified; the same spreadsheet again (a file whose rows the inbox has and the team did not discard), one entry
/// discarded as a repeated document (VC4); a proposal that is not authentic, not from its person's read in their language (decision 8), not on
/// this copy, or where no row fits, one entry unclassified with the forms to choose («No lo reconoce», DOC2). Otherwise every row of data with
/// the text of some cell is an entry (an empty one, or one of errors only, is not), armed from the copy with <see cref="FormDocumentRowPolicy"/>:
/// a load waits for its person and is sent as «i of k», one whose form cannot be sent to them (CE4-A, it does not count for the three) or past
/// the three loads is for the team, a repeated one is discarded with where it is, and one that fits no form or the model did not see stays
/// unclassified. Each keeps its row with the headers of its sheet as its reading, the sheet only when the book has more than one, and its place
/// in the book with what became of it, which a repeated registration answers. A spreadsheet none of whose rows fit becomes its rows when its
/// person chooses a form from the list (<see cref="RecognizeAsync"/>): the proposal of the interpretation that continued with that form fixed is
/// kept only for that form and armed from the inbox's own copy, the only place its rows are, with the same rules and the spreadsheet's arrival;
/// the same spreadsheet whose rows the inbox has by then is discarded as repeated, as at its registration (rule B4).
/// </summary>
internal sealed class FormReceivedDocumentRowPolicy(IFormReceivedDocumentReader reader, IFormSpreadsheetReader spreadsheets,
    FormReceivedDocumentCopyPolicy copies, FormDocumentRowPolicy rows, FormCatalogOptionsPolicy options, FormReceivedDocumentReplyPolicy replies,
    FormReceivedDocumentContentPolicy contents)
{
    public const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvMediaType = "text/csv";

    public static FormSpreadsheetFormat? Format(string mediaType) => mediaType switch
    {
        XlsxMediaType => FormSpreadsheetFormat.Xlsx,
        CsvMediaType => FormSpreadsheetFormat.Csv,
        _ => null,
    };

    /// <summary>Whether a row of data has the text of some cell: one without it (empty, or only errors) is no entry, like an empty row.</summary>
    public static bool HasText(FormDocumentSheetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Cells.Any(cell => cell.Error is null && !string.IsNullOrWhiteSpace(cell.Text));
    }

    /// <summary>The classification of a spreadsheet already copied (its first entry takes the copy's id), or null when its copy cannot be read.</summary>
    public async Task<FormReceivedDocumentRowClassification?> ClassifyAsync(StructuredDocumentRowsRegistrationRequest request, Guid copyId, string storageKey,
        CancellationToken ct)
    {
        var copy = request.Copy;
        if (request.NotRead == StructuredDocumentNotRead.Quota)
            return Single(request, copyId, new(FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Quota, null, false,
                new(copyId, StructuredDocumentReplyKind.Quota)));
        if (await reader.FindSpreadsheetAsync(copy.Sha256, ct) is { } earlier) return Repeated(request, copyId, earlier);
        if (Mapping(request) is not { } mapping) return await UnknownAsync(request, copyId, null, ct);
        if (await ArmAsync(request, copyId, storageKey, copy.Sha256, copy.Size, mapping, ct) is not { } armed) return null;
        return armed.Entries.Count > 0 ? armed : await UnknownAsync(request, copyId, mapping.DocumentType, ct);
    }

    /// <summary>
    /// A spreadsheet none of whose rows fit, as its single entry keeps it (unclassified, with the inbox's copy), recognized for the form its person
    /// chose: its rows from the proposal kept only for that form, its first row taking the entry's id, all of them arrived when the spreadsheet did
    /// (not with the tap that chose the form); the entry alone, discarded as repeated, when the inbox has the rows of the same spreadsheet from
    /// another file (rule B4); with no entries when the choice fits no row, or null when the proposal cannot be used or the copy cannot be read.
    /// </summary>
    public async Task<FormReceivedDocumentRowClassification?> RecognizeAsync(StructuredDocumentRowsRegistrationRequest request, FormReceivedDocumentStoredRow single,
        CancellationToken ct)
    {
        if (request.FocusDefinitionId is not { } focus || single is not { StorageKey: { } storageKey, Sha256: { } sha256, Size: { } size }
            || sha256 != request.Copy.Sha256) return null;
        if (await reader.FindSpreadsheetAsync(sha256, ct) is { } earlier) return Repeated(request, single.Id, earlier);
        if (Mapping(request) is not { } mapping) return null;
        return await ArmAsync(request with { ReceivedAtUtc = single.ReceivedAtUtc }, single.Id, storageKey, sha256, size,
            mapping with { Forms = [.. mapping.Forms.Where(form => form.DefinitionId == focus)] }, ct);
    }

    // Like a document's (decision 8): the forms come from the candidates read of its person, in their language, on this copy's sample.
    private static FormDocumentRowsProposal? Mapping(StructuredDocumentRowsRegistrationRequest request) =>
        request.Proposal is { } proposal && FormDocumentRowCodec.Authentic(proposal, out var mapping, out var read)
            && (read is null || read.Capability == BusinessFormDocumentAssistantCapabilitySource.CandidatesId
                && read.Read.Payload.UserId == request.SenderUserId && read.Read.Payload.Culture == request.Culture)
            && request.SampleSha256 == request.Copy.Sha256 ? mapping : null;

    // The rows of data with text armed from the copy with the mapping: no entries when none fits, null when the copy cannot be read.
    private async Task<FormReceivedDocumentRowClassification?> ArmAsync(StructuredDocumentRowsRegistrationRequest request, Guid firstId, string storageKey,
        string sha256, long size, FormDocumentRowsProposal mapping, CancellationToken ct)
    {
        if (Format(request.Copy.MediaType) is not { } format || await copies.ReadAsync(storageKey, sha256, size, ct) is not { } content) return null;
        var opened = spreadsheets.ReadSheets(content, format, ct);
        if (opened.IsFailure || opened.Value is not [var first, ..] || first.Sha256 != sha256) return null;
        var data = FormDocumentRowPolicy.DataRows(opened.Value);
        if (data.Count is 0 or > StructuredAttachmentLimits.MaximumLoadingRows) return null;
        var forms = await FormsAsync(mapping, request, ct);
        var plan = await rows.PrepareAsync(mapping, [.. data.Where(HasText)], request.Culture, ct,
            forms.Where(form => form.Value.Sendable).Select(form => form.Key).ToHashSet());
        if (!plan.AnyFits) return new([], null, new(StructuredDocumentReplyKind.Unknown));
        var sheets = FormDocumentRowPolicy.Sheets(opened.Value);
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, sheetRows) in sheets)
            if (sheetRows.Count > 0) headers.TryAdd(name, sheetRows[0].Number);
        return Rows(request, firstId, plan, data, headers, sheets.Count > 1, forms);
    }

    /// <summary>
    /// What the inbox keeps of a row by what became of it (VC1-A), with the steps of its history: a load waits for its person and was sent as
    /// «i of k»; one whose form cannot be sent to them, or past the three loads, is proposed for the team; a repeated one is discarded; one that
    /// fits no form stays unclassified as unknown and one the model did not see, as unseen.
    /// </summary>
    internal static FormReceivedDocumentRowClassified Classified(StructuredDocumentRowKind kind, int? load, int loads) => kind switch
    {
        StructuredDocumentRowKind.Load => new(FormReceivedDocumentState.Proposed, null, null,
            [(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.Sent, $"{load}/{loads}")]),
        StructuredDocumentRowKind.NotSendable => new(FormReceivedDocumentState.Proposed, null, FormReceivedDocumentNotSentReason.NotSendable,
            [(FormReceivedDocumentEventKind.Received, "notSendable")]),
        StructuredDocumentRowKind.OverLimit => new(FormReceivedDocumentState.Proposed, null, FormReceivedDocumentNotSentReason.OverLimit,
            [(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.OverLimit, null)]),
        StructuredDocumentRowKind.Repeated => new(FormReceivedDocumentState.Discarded, FormReceivedDocumentReplyPolicy.Duplicate, null,
            [(FormReceivedDocumentEventKind.Received, FormReceivedDocumentReplyPolicy.Duplicate)]),
        StructuredDocumentRowKind.Unseen => new(FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Unseen, null,
            [(FormReceivedDocumentEventKind.Received, FormReceivedDocumentReplyPolicy.Unseen)]),
        _ => new(FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Unknown, null,
            [(FormReceivedDocumentEventKind.Received, FormReceivedDocumentReplyPolicy.Unknown)]),
    };

    /// <summary>
    /// The reply of a spreadsheet already registered, from what it kept: its single entry as a document answers, or its rows in the order of
    /// the book, each with what became of it when it was registered (whatever the team did with it later), its form and summary, and for a
    /// repeated one where it is.
    /// </summary>
    public async Task<StructuredDocumentRowsRegistration> ReplayAsync(IReadOnlyList<FormReceivedDocumentStoredRow> stored, string culture, CancellationToken ct)
    {
        if (stored.All(row => row.RowNumber is null))
        {
            var reply = await replies.ReplayAsync(stored[0], culture, ct);
            return new(reply.Kind)
            {
                DocumentId = reply.DocumentId, Forms = reply.Forms, Duplicate = reply.Duplicate, WasAlreadyRegistered = true,
                ReplyExpiresAtUtc = reply.ReplyExpiresAtUtc,
            };
        }
        var entries = stored.Where(row => row.RowNumber is not null).OrderBy(row => row.RowOrder).ToArray();
        var proposals = entries.ToDictionary(row => row.Id, row => contents.ReadProposal(row.Id, row.ProtectedProposal));
        var versions = (await reader.ListVersionsAsync([.. proposals.Values.SelectMany(value => value?.Candidates ?? []).Select(value => value.VersionId).Distinct()], ct))
            .ToDictionary(version => version.VersionId);
        var replied = new List<StructuredDocumentRowRegistration>();
        var position = 0;
        foreach (var row in entries)
        {
            var proposal = proposals[row.Id];
            var candidate = proposal?.Candidates is [var first, ..] ? first : null;
            var kind = Replied(row.RowKind!.Value);
            replied.Add(new(row.Id, row.SheetName, row.RowNumber!.Value, kind)
            {
                Form = candidate is not null && versions.TryGetValue(candidate.VersionId, out var version) ? new(candidate.DefinitionId, candidate.VersionId,
                    FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.NameJson), culture, version.Culture)) : null,
                Position = kind == StructuredDocumentRowKind.Load ? ++position : null,
                Summary = candidate is null ? [] : Summary(candidate, culture),
                Duplicate = proposal?.Repetition is { RecordNumber: not null } repetition ? new(repetition.AtUtc, repetition.RecordNumber) : null,
                SameAs = proposal?.Repetition is { RowNumber: { } number } same ? new(same.RowSheet, number) : null,
            });
        }
        return new(StructuredDocumentReplyKind.Sure) { Rows = replied, WasAlreadyRegistered = true, ReplyExpiresAtUtc = entries.Max(row => row.ReplyExpiresAtUtc) };
    }

    // The forms of the mapping with whether its person can receive each (CE4-A): only their loads count for the three (rule 1).
    private async Task<Dictionary<Guid, (FormDocumentFormRow? Form, bool Sendable)>> FormsAsync(FormDocumentRowsProposal mapping,
        StructuredDocumentRowsRegistrationRequest request, CancellationToken ct)
    {
        var forms = new Dictionary<Guid, (FormDocumentFormRow? Form, bool Sendable)>();
        foreach (var definitionId in mapping.Forms.Select(form => form.DefinitionId).Distinct())
        {
            var found = await reader.FindActiveFormAsync(definitionId, ct);
            forms[definitionId] = (found, found is { AppOnly: false } && (await options.PrepareAsync(request.SenderUserId, request.Culture,
                FormDraftPolicy.Read<FormVersionSchema>(found.SchemaJson), found.Culture, ct, inspectOnly: true)).CanSend);
        }
        return forms;
    }

    private static FormReceivedDocumentRowClassification Rows(StructuredDocumentRowsRegistrationRequest request, Guid copyId, FormDocumentRowPlan plan,
        IReadOnlyList<FormDocumentSheetRow> data, IReadOnlyDictionary<string, int> headers, bool sheets,
        IReadOnlyDictionary<Guid, (FormDocumentFormRow? Form, bool Sendable)> forms)
    {
        var culture = request.Culture;
        var ids = new Dictionary<(string, int), Guid>();
        foreach (var prepared in plan.Rows) ids[(prepared.Sheet, prepared.Number)] = ids.Count == 0 ? copyId : Guid.CreateVersion7();
        var loads = plan.Rows.Count(row => row.Outcome == FormDocumentRowOutcome.Load);
        var position = 0;
        var entries = new List<FormReceivedDocumentRowEntry>();
        var replied = new List<StructuredDocumentRowRegistration>();
        foreach (var prepared in plan.Rows)
        {
            var id = ids[(prepared.Sheet, prepared.Number)];
            var form = prepared.Candidate is { } mapped ? forms.GetValueOrDefault(mapped.DefinitionId).Form : null;
            var kind = Kind(prepared.Outcome);
            var source = data.First(row => row.Sheet == prepared.Sheet && row.Number == prepared.Number);
            var sheet = sheets ? prepared.Sheet : null;
            var sameAs = prepared.SameAs is { } same ? (Id: ids[(same.Sheet, same.Number)], Sheet: sheets ? same.Sheet : null, same.Number) : default;
            var repetition = prepared.SameAs is not null ? new FormDocumentRepetition(request.ReceivedAtUtc, null, null, sameAs.Id) { RowSheet = sameAs.Sheet, RowNumber = sameAs.Number }
                : prepared.Repetition;
            var proposal = prepared.Candidate is { } candidate
                ? new FormDocumentPreparedProposal(FormDocumentEntryProposal.Sure, plan.DocumentType, [candidate]) { Repetition = repetition } : null;
            var load = kind == StructuredDocumentRowKind.Load ? ++position : (int?)null;
            var classified = Classified(kind, load, loads);
            entries.Add(new(id, sheet, prepared.Number, entries.Count + 1, Stored(kind), classified.State, classified.ReasonCode, proposal, classified.NotSent,
                kind == StructuredDocumentRowKind.Load, FormDocumentRowReading.From(source, headers.GetValueOrDefault(source.Sheet, source.Number), culture),
                plan.DocumentType, classified.Steps));
            replied.Add(new(id, sheet, prepared.Number, kind)
            {
                Form = prepared.Candidate is not null && form is not null ? new(form.DefinitionId, form.VersionId, FormDocumentFormPolicy.Name(form, culture)) : null,
                Position = load,
                Summary = prepared.Candidate is { } summarized ? Summary(summarized, culture) : [],
                Duplicate = prepared.SameAs is null && prepared.Repetition is { } found ? new(found.AtUtc, found.RecordNumber) : null,
                SameAs = prepared.SameAs is null ? null : new(sameAs.Sheet, sameAs.Number),
            });
        }
        return new(entries, null, new(StructuredDocumentReplyKind.Sure) { Rows = replied, ReplyExpiresAtUtc = request.ReplyExpiresAtUtc });
    }

    // The same spreadsheet again (a file whose rows the inbox has and the team did not discard): one entry discarded as a repeated document (VC4),
    // with when the earlier one arrived.
    private static FormReceivedDocumentRowClassification Repeated(StructuredDocumentRowsRegistrationRequest request, Guid copyId,
        FormReceivedDocumentStoredRow earlier) =>
        Single(request, copyId, new(FormReceivedDocumentState.Discarded, FormReceivedDocumentReplyPolicy.Duplicate,
            new FormDocumentPreparedProposal(FormDocumentEntryProposal.Unknown, null, []) { Repetition = new(earlier.ReceivedAtUtc, null, null, earlier.Id) },
            false, new(copyId, StructuredDocumentReplyKind.Duplicate) { Duplicate = new(earlier.ReceivedAtUtc, null) }));

    // None fits, or the proposal cannot be used: one entry, as a document nobody recognized, with the forms the person can choose.
    private async Task<FormReceivedDocumentRowClassification> UnknownAsync(StructuredDocumentRowsRegistrationRequest request, Guid copyId, string? documentType,
        CancellationToken ct) =>
        Single(request, copyId, new(FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Unknown,
            documentType is null ? null : new FormDocumentPreparedProposal(FormDocumentEntryProposal.Unknown, documentType, []), false,
            new(copyId, StructuredDocumentReplyKind.Unknown) { Forms = await replies.FormsAsync(request.Culture, ct) }));

    private static FormReceivedDocumentRowClassification Single(StructuredDocumentRowsRegistrationRequest request, Guid copyId,
        FormReceivedDocumentClassification single) =>
        new([], single, new(single.Reply.Kind)
        {
            DocumentId = copyId, Forms = single.Reply.Forms, Duplicate = single.Reply.Duplicate, ReplyExpiresAtUtc = request.ReplyExpiresAtUtc,
        });

    private static StructuredDocumentRowKind Kind(FormDocumentRowOutcome outcome) => outcome switch
    {
        FormDocumentRowOutcome.Load => StructuredDocumentRowKind.Load,
        FormDocumentRowOutcome.NotSendable => StructuredDocumentRowKind.NotSendable,
        FormDocumentRowOutcome.OverLimit => StructuredDocumentRowKind.OverLimit,
        FormDocumentRowOutcome.Repeated => StructuredDocumentRowKind.Repeated,
        FormDocumentRowOutcome.Unmatched => StructuredDocumentRowKind.Unmatched,
        _ => StructuredDocumentRowKind.Unseen,
    };

    // What became of a row, as its entry keeps it and as a repeated registration answers it.
    private static FormReceivedDocumentRowKind Stored(StructuredDocumentRowKind kind) => kind switch
    {
        StructuredDocumentRowKind.Load => FormReceivedDocumentRowKind.Load,
        StructuredDocumentRowKind.NotSendable => FormReceivedDocumentRowKind.NotSendable,
        StructuredDocumentRowKind.OverLimit => FormReceivedDocumentRowKind.OverLimit,
        StructuredDocumentRowKind.Repeated => FormReceivedDocumentRowKind.Repeated,
        StructuredDocumentRowKind.Unmatched => FormReceivedDocumentRowKind.Unmatched,
        _ => FormReceivedDocumentRowKind.Unseen,
    };

    private static StructuredDocumentRowKind Replied(FormReceivedDocumentRowKind kind) => kind switch
    {
        FormReceivedDocumentRowKind.Load => StructuredDocumentRowKind.Load,
        FormReceivedDocumentRowKind.NotSendable => StructuredDocumentRowKind.NotSendable,
        FormReceivedDocumentRowKind.OverLimit => StructuredDocumentRowKind.OverLimit,
        FormReceivedDocumentRowKind.Repeated => StructuredDocumentRowKind.Repeated,
        FormReceivedDocumentRowKind.Unmatched => StructuredDocumentRowKind.Unmatched,
        _ => StructuredDocumentRowKind.Unseen,
    };

    // The first two data of a load, as its person reads them (decision 9).
    private static string[] Summary(FormDocumentPreparedCandidate candidate, string culture) =>
        [.. FormDocumentRowPolicy.Summary(candidate).Select(value => FormReceivedDocumentReplyPolicy.Display(value, culture)).OfType<string>()];
}

/// <summary>What the inbox keeps of a row by what became of it: its state, its reason, why it was not sent and the steps of its history.</summary>
internal sealed record FormReceivedDocumentRowClassified(FormReceivedDocumentState State, string? ReasonCode, FormReceivedDocumentNotSentReason? NotSent,
    IReadOnlyList<(FormReceivedDocumentEventKind Kind, string? Detail)> Steps);

/// <summary>The rows of a spreadsheet as entries, or the single entry of one without rows, and what to tell the person.</summary>
internal sealed record FormReceivedDocumentRowClassification(IReadOnlyList<FormReceivedDocumentRowEntry> Entries, FormReceivedDocumentClassification? Single,
    StructuredDocumentRowsRegistration Reply)
{
    public override string ToString() => "FormReceivedDocumentRowClassification { Content = [REDACTED] }";
}
