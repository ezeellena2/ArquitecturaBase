using System.Globalization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

// channel-documents (task 17a): what the app does with the outcome, checked in memory. Formularios receives the document's or the spreadsheet's
// proposal and, only from a context that just loads the file, a turn without one (WhatsAppAssistantTerminalPolicy.HandlesDocumentWithoutProposal
// and RegistersRows); with its request, a turn without a proposal never reaches the inbox, so there is no «No lo reconoce» list («none»). A
// document's proposal is classified as formularios classifies it (FormReceivedDocumentReplyPolicy, without the database: no repeated response
// or retired form exists in the fixture), its candidates are the forms one candidates read of the turn showed the person, and each expected fact
// is in its question, typed; no value is a number the document does not print. A spreadsheet's rows go to their forms with the column of each
// question, as formularios arms each row without AI. Only fixed IDs and booleans leave it.
internal static class ChannelDocumentOracle
{
    private static readonly AssistantCapabilityReference CandidatesRead = new(QualificationChannelDocuments.Candidates, 1);
    private static readonly AssistantCapabilityReference EntryProposal = new(QualificationChannelDocuments.ProposeEntry, 1);
    private static readonly AssistantCapabilityReference RowsProposal = new(QualificationChannelDocuments.ProposeRows, 1);

    // FormReceivedDocumentReplyPolicy.ClassifyAsync on what reaches the inbox: no proposal, «unknown» or no candidate is «No lo reconoce» when the
    // reading is legible and «No se lee» when it is not; a first candidate that leaves a required question without a value in a reading that is
    // not legible, «No se lee»; then the question of a doubt or the form of a sure one. FormDocumentProposalPolicy.PrepareAsync gives each
    // question the candidates read showed a value, null when the model left it out, before MissingRequired looks for a required one in null:
    // an omitted required question counts as an empty one.
    public static string EntryDecision(AssistantObservedProposal? observed, AssistantChannelContext context, StructuredDocumentReading reading)
    {
        var legible = reading.Readability == StructuredDocumentReadability.Readable;
        if (observed is null) return !QualificationChannelDocuments.LoadsOnly(context, document: true) ? "none" : legible ? "unknown" : "unreadable";
        if (observed.Capability != EntryProposal || observed.Payload is not FormDocumentEntryProposal proposal) return "none";
        if (proposal is not { Candidates.Count: > 0 } || proposal.Outcome == FormDocumentEntryProposal.Unknown) return legible ? "unknown" : "unreadable";
        var first = proposal.Candidates[0];
        var form = Read(observed, proposal.ReadOrdinal)?.Forms.SingleOrDefault(item => item.DefinitionId == first.DefinitionId && item.VersionId == first.VersionId);
        var missing = form is null || form.Questions.Any(question => question.Required
            && first.Values.SingleOrDefault(value => value.FieldId == question.FieldId)?.Value is null);
        if (!legible && missing) return "unreadable";
        return proposal.Outcome == FormDocumentEntryProposal.Doubt ? "doubt" : "sure";
    }

    // FormReceivedDocumentRowPolicy.ClassifyAsync on what reaches the inbox: some row that fits a form loads (FormDocumentRowPlan.AnyFits); none,
    // or no proposal, is «No lo reconoce» with the list.
    public static string RowsDecision(AssistantObservedProposal? observed, AssistantChannelContext context) => observed is null
        ? QualificationChannelDocuments.LoadsOnly(context, document: false) ? "unknown" : "none"
        : observed.Capability == RowsProposal && observed.Payload is FormDocumentRowsProposal rows ? rows.Forms.Any(form => form.Rows.Count > 0) ? "rows" : "unknown"
        : "none";

    // The sealed context is the harness's copy of the one the channel builds for the case (QualificationChannelDocuments.Context), with its focus.
    public static IReadOnlyList<CheckEvidence> ContextChecks(AssistantChannelContext expected, TurnMetadata technical) =>
    [
        new("loading-context-matches-the-channel", technical.OfferedReferences.SequenceEqual(expected.AllowedCapabilities)
            && technical.PreloadedReferences.SequenceEqual(expected.PreloadedCapabilities ?? [])
            && technical.Terminals.SequenceEqual(expected.AllowedTerminals) && technical.FocusDefinitionId == expected.FocusDefinitionId
            && technical.InteractionMode == AssistantInteractionMode.Assistant && technical.SubjectUserId == expected.SubjectUserId),
    ];

    // context: the one sealed for the turn; without a proposal, only one that just loads the document gets an answer from the inbox.
    public static IReadOnlyList<CheckEvidence> EntryChecks(ChannelCase testCase, AssistantChannelContext context, AssistantObservedProposal? observed,
        StructuredDocumentReading reading, DocumentFixture printed, QualificationChannelFixture forms, Guid subject)
    {
        var expected = testCase.Loading!;
        var proposal = observed?.Payload as FormDocumentEntryProposal;
        var read = Read(observed, proposal?.ReadOrdinal);
        var decision = EntryDecision(observed, context, reading);
        var candidates = proposal?.Candidates ?? [];
        var checks = new List<CheckEvidence>
        {
            new("expected-loading-payload", observed is null
                ? expected.Decision is "unknown" or "unreadable" && QualificationChannelDocuments.LoadsOnly(context, document: true)
                : proposal is not null && observed.Capability == EntryProposal),
            new("candidates-from-the-observed-read", candidates.Count == 0 || read is not null && FromRead(read, subject, testCase.Culture,
                candidates.Select(candidate => (candidate.DefinitionId, candidate.VersionId, candidate.ExpectedDefinitionRevision)))),
            new("app-decision", decision == expected.Decision),
            new("expected-forms", expected.Decision switch
            {
                "sure" => candidates.Count >= 1 && Is(candidates[0], forms.Form(expected.Forms[0])),
                "doubt" => candidates.Count == 2 && expected.Forms.All(alias => candidates.Count(candidate => Is(candidate, forms.Form(alias))) == 1),
                _ => true,
            }),
            new("no-invented-values", candidates.All(candidate => candidate.Values.All(value => value.Value is null || FromDocument(value.Type, value.Value, printed)))),
            new("document-type-without-numbers", proposal is null || proposal.DocumentType.Length <= FormDocumentEntryProposal.MaxDocumentTypeLength
                && !DocumentReadingOracle.Numbers(proposal.DocumentType).Any(number => number.Length >= 4)),
        };
        // A sure form keeps each fact in its question; each form of a doubt too, since the one the person chooses goes out with what it read.
        var keeping = expected.Decision == "sure" ? candidates.Take(1) : expected.Decision == "doubt" ? candidates : [];
        foreach (var (fact, key) in expected.Facts ?? new Dictionary<string, string>())
            checks.Add(new("fact-" + fact + "-in-its-question", keeping.Any() && keeping.All(candidate =>
                forms.Forms.SingleOrDefault(form => form.DefinitionId == candidate.DefinitionId) is { } form && form.Fields.TryGetValue(key, out var field)
                && candidate.Values.SingleOrDefault(value => value.FieldId == field) is { Value: { } value } typed
                && printed.Facts.TryGetValue(fact, out var written) && FactMatches(typed.Type, value, written))));
        return checks;
    }

    // context: the one sealed for the turn, with the chosen form as its focus; without a proposal, only one that just loads the spreadsheet
    // reaches the list, and with its request «No lo reconoce» needs the rows' proposal with no form.
    public static IReadOnlyList<CheckEvidence> RowsChecks(ChannelCase testCase, AssistantChannelContext context, AssistantObservedProposal? observed,
        StructuredSpreadsheet sheet, QualificationChannelFixture forms, Guid subject)
    {
        var expected = testCase.Loading!;
        var proposal = observed?.Payload as FormDocumentRowsProposal;
        var read = Read(observed, proposal?.ReadOrdinal);
        var named = proposal?.Forms.Where(form => form.Rows.Count > 0).ToArray() ?? [];
        var dataRows = sheet.Rows.Where(row => row.Number > 1).Select(row => row.Number).ToHashSet();
        var wanted = expected.Rows ?? [];
        var checks = new List<CheckEvidence>
        {
            new("expected-loading-payload", observed is null ? expected.Decision == "unknown" && QualificationChannelDocuments.LoadsOnly(context, document: false)
                : proposal is not null && observed.Capability == RowsProposal),
            new("forms-from-the-observed-read", named.Length == 0 || read is not null && FromRead(read, subject, testCase.Culture,
                named.Select(form => (form.DefinitionId, form.VersionId, form.ExpectedDefinitionRevision)))),
            new("app-decision", RowsDecision(observed, context) == expected.Decision),
            new("rows-in-their-forms", named.Length == wanted.Length && wanted.All(item => named.Count(form => form.DefinitionId == forms.Form(item.Form).DefinitionId
                && form.VersionId == forms.Form(item.Form).VersionId && form.Rows.Order().SequenceEqual(item.Rows.Order())
                && form.Rows.All(number => sheet.Rows.Any(row => row.Number == number && row.Sheet == form.Sheet))) == 1)),
            new("columns-in-their-questions", wanted.All(item => named.SingleOrDefault(form => form.DefinitionId == forms.Form(item.Form).DefinitionId) is { } form
                && Mapped(form, forms.Form(item.Form), item))),
            new("every-other-row-unmatched", expected.Decision != "rows" || proposal is not null
                && proposal.Unmatched.SelectMany(set => set.Rows).ToHashSet().SetEquals(dataRows.Except(wanted.SelectMany(item => item.Rows)))),
            new("document-type-without-numbers", proposal is null || !DocumentReadingOracle.Numbers(proposal.DocumentType).Any(number => number.Length >= 4)),
        };
        if (wanted.Any(item => item.Dubious is not null))
            checks.Add(new("dubious-columns-read-with-doubts", wanted.Where(item => item.Dubious is not null).All(item =>
                named.SingleOrDefault(form => form.DefinitionId == forms.Form(item.Form).DefinitionId) is not { } form
                || item.Dubious!.All(dubious => !form.Columns.Any(column => column.FieldId == forms.Form(item.Form).Fields[dubious.Key])
                    || form.Doubtful.Contains(dubious.Value, StringComparer.Ordinal)))));
        if (expected.Note is { } note)
            checks.Add(new("untrusted-cell-note-in-prepared-sheet", sheet.Rows.Sum(row => row.Cells.Values.Count(value => string.Equals(value, note, StringComparison.Ordinal))) == 1));
        if (context.FocusDefinitionId is { } chosen)
            checks.Add(new("focused-read-shows-only-the-chosen-form", observed is not null && observed.ObservedReads.Any(item => item.Capability == CandidatesRead)
                && observed.ObservedReads.Where(item => item.Capability == CandidatesRead).Select(item => item.Payload).OfType<FormDocumentReadPayload>()
                    .All(payload => payload.Forms.Count == 1 && payload.Forms[0].DefinitionId == chosen)));
        return checks;
    }

    // The candidates read the proposal names: same turn, the person who sent the file and the conversation's culture.
    private static FormDocumentReadPayload? Read(AssistantObservedProposal? observed, int? ordinal) => observed is null || ordinal is null ? null
        : observed.ObservedReads.Where(item => item.TurnId == observed.TurnId && item.Ordinal == ordinal && item.Capability == CandidatesRead)
            .Select(item => item.Payload).OfType<FormDocumentReadPayload>().SingleOrDefault();

    private static bool FromRead(FormDocumentReadPayload read, Guid subject, string culture, IEnumerable<(Guid Definition, Guid Version, long Revision)> named) =>
        read.UserId == subject && read.Culture == culture && (read.IsComplete ? read.State == "complete" : read.State == "partial")
        && named.All(item => read.Forms.Any(form => form.DefinitionId == item.Definition && form.VersionId == item.Version && form.DefinitionRevision == item.Revision));

    private static bool Is(FormDocumentEntryCandidate candidate, QualificationForm form) =>
        candidate.DefinitionId == form.DefinitionId && candidate.VersionId == form.VersionId;

    // Exactly the expected columns, plus a dubious one only in its own question; nothing else is mapped.
    private static bool Mapped(FormDocumentRowForm form, QualificationForm fixture, ChannelRowsExpectation item)
    {
        var allowed = item.Columns.ToDictionary(column => fixture.Fields[column.Key], column => column.Value);
        var dubious = (item.Dubious ?? new Dictionary<string, string>()).ToDictionary(column => fixture.Fields[column.Key], column => column.Value);
        return form.Columns.Select(column => column.FieldId).Distinct().Count() == form.Columns.Count
            && allowed.All(column => form.Columns.Count(mapped => mapped.FieldId == column.Key && mapped.Column == column.Value) == 1)
            && form.Columns.All(mapped => allowed.TryGetValue(mapped.FieldId, out var letter) && letter == mapped.Column
                || dubious.TryGetValue(mapped.FieldId, out var maybe) && maybe == mapped.Column);
    }

    // A fact as its question takes it: an amount by its value, a date by its day, a key or a CUIT by its digits.
    public static bool FactMatches(string type, string value, string written) => type switch
    {
        "decimal" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && Amount(written) is { } printed && amount == printed,
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            && DateOnly.TryParseExact(written, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var printedDay) && day == printedDay,
        _ => DocumentReadingOracle.Digits(value) is { Length: > 0 } digits && digits == DocumentReadingOracle.Digits(written),
    };

    // A printed amount as the fixture writes it, «$ 152.340,50»: the dots group and the comma separates the cents.
    private static decimal? Amount(string written)
    {
        var plain = new string([.. written.Where(character => char.IsAsciiDigit(character) || character == ',')]).Replace(',', '.');
        return decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    // A value read from the document: an amount's digits, a date's day as printed (dd/MM/yyyy) and every number of a text are inside a number
    // the document prints; a choice or a yes or no has no digits to invent.
    public static bool FromDocument(string type, string value, DocumentFixture printed)
    {
        var numbers = printed.Text.SelectMany(DocumentReadingOracle.Numbers).ToArray();
        bool Printed(string digits) => digits.Length > 0 && numbers.Any(number => number.Contains(digits, StringComparison.Ordinal));
        return type switch
        {
            "decimal" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                && Printed(DocumentReadingOracle.Digits(amount.ToString("0.##", CultureInfo.InvariantCulture))),
            "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && Printed(day.ToString("ddMMyyyy", CultureInfo.InvariantCulture)),
            "boolean" or "choice" => true,
            _ => DocumentReadingOracle.Numbers(value).All(Printed),
        };
    }
}
