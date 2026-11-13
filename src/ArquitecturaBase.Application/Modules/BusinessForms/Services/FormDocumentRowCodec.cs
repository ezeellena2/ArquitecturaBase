using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The exact schemas and the closed codec of forms.documents.propose-rows@1 (decision 9, VC1-A). A proposal is normalized against the latest
/// documents read of the turn and the sample to load the model saw: each form observed in that read, in a sheet of the sample, with rows of
/// data the sample showed in that sheet (never its headers, its first row), named once among all the forms and the rows that fit none, each
/// question of the form at most once with a column its sheet shows (never a catalog question: the server fills it) and the doubtful columns
/// among those. A problem goes back to the model with its path, never trimmed; the rows of data the model saw are the server's.
/// </summary>
internal static class FormDocumentRowCodec
{
    public const string UnknownRow = "unknownRow";
    // Each form of a sheet, each sheet of the rows that fit none and of the rows seen holds a row of its own: never more than the rows.
    public const int MaxRows = StructuredAttachmentLimits.MaximumLoadingRows;
    private const int MaxSheetLength = 120;
    private const int MaxRowNumber = 1_048_576;
    private static readonly string[] Codes = [.. FormDocumentEntryIssue.Codes, UnknownRow];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly object RowsSchema = new
    {
        type = "array", maxItems = MaxRows,
        items = new { type = "integer", description = "A row number of the sheet, as the spreadsheet shows it." },
    };
    private static readonly object SheetSchema = new { type = "string", description = "The sheet of the rows, exactly as the spreadsheet shows it; empty in a CSV." };
    public static string InputSchemaJson { get; } = JsonSerializer.Serialize(Obj(
        ("forms", Arr(Obj(("definitionId", Str()), ("versionId", Str()), ("sheet", SheetSchema), ("rows", RowsSchema),
            ("columns", Arr(Obj(("fieldId", Str()), ("column", new { type = "string", description = "The column letter that fills the question, as the spreadsheet shows it (A, B…)." })),
                FormDocumentCapabilityCodec.MaxQuestions)),
            ("doubtful", Arr(new { type = "string", description = "A column letter of this form read with doubts." }, FormDocumentCapabilityCodec.MaxQuestions))), MaxRows)),
        ("unmatched", Arr(Obj(("sheet", SheetSchema), ("rows", RowsSchema)), MaxRows)),
        ("documentType", Str())), Options);
    private static readonly object SetSchema = Obj(("sheet", Str()), ("rows", Arr(new { type = "integer" }, MaxRows)));
    public static string OutputSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("proposed")), ("readOrdinal", new { type = "integer" }),
        ("forms", Arr(Obj(("definitionId", Str()), ("versionId", Str()), ("expectedDefinitionRevision", new { type = "integer" }), ("sheet", Str()),
            ("rows", Arr(new { type = "integer" }, MaxRows)),
            ("columns", Arr(Obj(("fieldId", Str()), ("type", Str(FormDocumentCapabilityCodec.QuestionTypes)), ("column", Str())), FormDocumentCapabilityCodec.MaxQuestions)),
            ("doubtful", Arr(Str(), FormDocumentCapabilityCodec.MaxQuestions))), MaxRows)),
        ("unmatched", Arr(SetSchema, MaxRows)), ("seen", Arr(SetSchema, MaxRows)), ("documentType", Str())), Options);

    /// <summary>A sample to load: the rows of every sheet, each with its sheet, and at most <see cref="MaxRows"/> rows of data.</summary>
    public static bool Loading(StructuredSpreadsheet sample) =>
        sample.DataRowCount is >= 1 and <= MaxRows && sample.Rows.All(row => row.Sheet is not null);

    /// <summary>How many rows a proposal names: those of its forms and those that fit none.</summary>
    public static int Named(FormDocumentRowsProposal proposal) => proposal.Forms.Sum(form => form.Rows.Count) + proposal.Unmatched.Sum(set => set.Rows.Count);

    public static Result<FormDocumentRowsArguments> ParseArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Has(root, "forms", "unmatched", "documentType") || root.GetProperty("forms") is not { ValueKind: JsonValueKind.Array } forms
                || root.GetProperty("unmatched") is not { ValueKind: JsonValueKind.Array } unmatched
                || forms.EnumerateArray().Any(form => !Has(form, "definitionId", "versionId", "sheet", "rows", "columns", "doubtful")
                    || form.GetProperty("columns") is not { ValueKind: JsonValueKind.Array } columns
                    || columns.EnumerateArray().Any(column => !Has(column, "fieldId", "column")))
                || unmatched.EnumerateArray().Any(set => !Has(set, "sheet", "rows")))
                return FormRecordErrors.InvalidResponse;
            var value = root.Deserialize<FormDocumentRowsArguments>(Options);
            return value is { Forms: not null, Unmatched: not null, DocumentType: not null }
                && value.Forms.All(form => form is { Sheet: not null, Rows: not null, Columns: not null, Doubtful: not null }
                    && form.Columns.All(column => column is { Column: not null }) && form.Doubtful.All(letter => letter is not null))
                && value.Unmatched.All(set => set is { Sheet: not null, Rows: not null })
                ? value : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    /// <summary>
    /// The proposal normalized against the latest documents read of the turn and the sample to load, or the problems to fix with their paths.
    /// Rows go in order; columns in capitals, with the type of their question; the rows that fit no form, joined by sheet.
    /// </summary>
    public static (FormDocumentRowsProposal? Proposal, IReadOnlyList<FormDocumentEntryIssue> Issues) Normalize(FormDocumentRowsArguments arguments,
        FormObservedDocumentRead? observed, StructuredSpreadsheet sample)
    {
        var issues = new List<FormDocumentEntryIssue>();
        var sheets = Sheets(sample);
        var named = new HashSet<(string Sheet, int Row)>();
        var documentType = arguments.DocumentType.Trim();
        if (!FormAssistantCapabilityCodec.Text(documentType, FormDocumentEntryProposal.MaxDocumentTypeLength))
            issues.Add(new("documentType", FormDocumentEntryIssue.Invalid));
        else if (FormMissingRequestPrivacyPolicy.HasContactDetails(documentType, null) || documentType.Count(char.IsAsciiDigit) >= 4)
            issues.Add(new("documentType", FormDocumentEntryIssue.PersonalData));
        if (arguments.Forms.Count > MaxRows) issues.Add(new("forms", FormDocumentEntryIssue.Count));
        if (arguments.Unmatched.Count > MaxRows) issues.Add(new("unmatched", FormDocumentEntryIssue.Count));
        var forms = new List<FormDocumentRowForm>();
        for (var index = 0; index < arguments.Forms.Count; index++)
        {
            var form = arguments.Forms[index];
            var path = $"forms[{index}]";
            if (arguments.Forms.Take(index).Any(previous => previous.DefinitionId == form.DefinitionId && previous.Sheet == form.Sheet))
            {
                issues.Add(new(path + ".definitionId", FormDocumentEntryIssue.Repeated));
                continue;
            }
            var candidate = observed?.Payload.Forms.SingleOrDefault(read => read.DefinitionId == form.DefinitionId && read.VersionId == form.VersionId);
            if (candidate is null)
            {
                issues.Add(new(path, FormDocumentEntryIssue.NotObserved));
                continue;
            }
            if (!sheets.TryGetValue(form.Sheet, out var sheet))
            {
                issues.Add(new(path + ".sheet", FormDocumentEntryIssue.Invalid));
                continue;
            }
            var rows = Rows(form.Sheet, form.Rows, sheet.Rows, path + ".rows", named, issues);
            var columns = new List<FormDocumentRowColumn>();
            for (var position = 0; position < form.Columns.Count; position++)
            {
                var column = form.Columns[position];
                var columnPath = $"{path}.columns[{position}]";
                var letter = Letter(column.Column);
                if (form.Columns.Take(position).Any(previous => previous.FieldId == column.FieldId))
                    issues.Add(new(columnPath + ".fieldId", FormDocumentEntryIssue.Repeated));
                else if (candidate.Questions.SingleOrDefault(question => question.FieldId == column.FieldId) is not { } question)
                    issues.Add(new(columnPath + ".fieldId", FormDocumentEntryIssue.UnknownQuestion));
                else if (question.Catalog is not null)
                    issues.Add(new(columnPath + ".fieldId", FormDocumentEntryIssue.FilledByServer));
                else if (!sheet.Columns.Contains(letter))
                    issues.Add(new(columnPath + ".column", FormDocumentEntryIssue.Invalid));
                else if (columns.Any(previous => previous.Column == letter))
                    issues.Add(new(columnPath + ".column", FormDocumentEntryIssue.Repeated));
                else columns.Add(new(question.FieldId, question.Type, letter));
            }
            if (form.Columns.Count == 0) issues.Add(new(path + ".columns", FormDocumentEntryIssue.Count));
            var doubtful = new List<string>();
            for (var position = 0; position < form.Doubtful.Count; position++)
            {
                var letter = Letter(form.Doubtful[position]);
                if (doubtful.Contains(letter)) issues.Add(new($"{path}.doubtful[{position}]", FormDocumentEntryIssue.Repeated));
                else if (!form.Columns.Any(column => Letter(column.Column) == letter)) issues.Add(new($"{path}.doubtful[{position}]", FormDocumentEntryIssue.Invalid));
                else doubtful.Add(letter);
            }
            forms.Add(new(candidate.DefinitionId, candidate.VersionId, candidate.DefinitionRevision, form.Sheet, rows, columns, doubtful));
        }
        var unmatched = new List<FormDocumentRowSet>();
        for (var index = 0; index < arguments.Unmatched.Count; index++)
        {
            var set = arguments.Unmatched[index];
            var path = $"unmatched[{index}]";
            if (!sheets.TryGetValue(set.Sheet, out var sheet))
            {
                issues.Add(new(path + ".sheet", FormDocumentEntryIssue.Invalid));
                continue;
            }
            var rows = Rows(set.Sheet, set.Rows, sheet.Rows, path + ".rows", named, issues);
            var existing = unmatched.FindIndex(other => other.Sheet == set.Sheet);
            if (existing >= 0) unmatched[existing] = unmatched[existing] with { Rows = [.. unmatched[existing].Rows.Concat(rows).Order()] };
            else unmatched.Add(new(set.Sheet, rows));
        }
        if (issues.Count != 0 || observed is null && arguments.Forms.Count != 0)
            return (null, issues.Count == 0 ? [new("forms", FormDocumentEntryIssue.NotObserved)] : issues);
        var seen = sample.Rows.Select(row => row.Sheet!).Distinct(StringComparer.Ordinal).Where(name => sheets[name].Rows.Count > 0)
            .Select(name => new FormDocumentRowSet(name, sheets[name].Rows)).ToArray();
        return (new FormDocumentRowsProposal("proposed", observed?.ReadOrdinal ?? 0, forms, unmatched.Where(set => set.Rows.Count > 0).ToArray(), seen,
            documentType), []);
    }

    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference reference, string json)
    {
        if (reference is not { Id: BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, Version: BusinessFormDocumentAssistantCapabilitySource.Version })
            return FormRecordErrors.InvalidResponse;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Has(root, "issues"))
            {
                var rejection = root.Deserialize<FormDocumentEntryRejection>(Options);
                return rejection is { Issues: { Count: > 0 and <= 50 } issues } && issues.All(issue => issue is { Path.Length: > 0 and <= 120 }
                    && Codes.Contains(issue.Code, StringComparer.Ordinal)) ? rejection : FormRecordErrors.InvalidResponse;
            }
            if (!ProposalShape(root)) return FormRecordErrors.InvalidResponse;
            var proposal = root.Deserialize<FormDocumentRowsProposal>(Options);
            return proposal is not null && ValidProposal(proposal) ? proposal : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    // What the owner would have returned: forms of a read, with rows the model saw named once, valid columns of read types and the doubtful
    // among them, and the rows it saw, each sheet once.
    internal static bool ValidProposal(FormDocumentRowsProposal value)
    {
        if (value is not { State: "proposed", ReadOrdinal: >= 0, Forms: { Count: <= MaxRows } forms, Unmatched: { Count: <= MaxRows } unmatched,
            Seen: { Count: <= MaxRows } seen } || !FormAssistantCapabilityCodec.Text(value.DocumentType, FormDocumentEntryProposal.MaxDocumentTypeLength)
            || forms.Count != 0 && value.ReadOrdinal < 1 || !ValidSets(unmatched, allowEmpty: false) || !ValidSets(seen, allowEmpty: false)
            || forms.Select(form => (form.DefinitionId, form.Sheet)).Distinct().Count() != forms.Count)
            return false;
        var visible = seen.SelectMany(set => set.Rows.Select(row => (set.Sheet, row))).ToHashSet();
        var rows = forms.SelectMany(form => form.Rows.Select(row => (form.Sheet, row))).Concat(unmatched.SelectMany(set => set.Rows.Select(row => (set.Sheet, row)))).ToArray();
        return rows.Distinct().Count() == rows.Length && rows.All(visible.Contains)
            && forms.All(form => form.DefinitionId != Guid.Empty && form.VersionId != Guid.Empty && form.ExpectedDefinitionRevision is >= 1 and <= 9007199254740991
                && ValidRows(form.Rows, allowEmpty: false) && ValidSheet(form.Sheet) && form.Columns is { Count: >= 1 and <= FormDocumentCapabilityCodec.MaxQuestions } columns
                && columns.Select(column => column.FieldId).Distinct().Count() == columns.Count && columns.Select(column => column.Column).Distinct().Count() == columns.Count
                && columns.All(column => column.FieldId != Guid.Empty && FormDocumentCapabilityCodec.QuestionTypes.Contains(column.Type, StringComparer.Ordinal)
                    && column.Column == Letter(column.Column) && FormAuthoringSpreadsheetPolicy.Number(column.Column) is >= 1 and <= FormCatalogImportLimits.MaximumColumns)
                && form.Doubtful.Distinct().Count() == form.Doubtful.Count && form.Doubtful.All(letter => columns.Any(column => column.Column == letter)));
    }

    /// <summary>
    /// The same check on a proposal the engine already returned, for whoever registers its rows: its forms are in the latest documents read
    /// of its turn, the one it was normalized against; one without forms needs no read.
    /// </summary>
    public static bool Authentic(AssistantObservedProposal proposal, [NotNullWhen(true)] out FormDocumentRowsProposal? payload) =>
        Authentic(proposal, out payload, out _);

    /// <summary>The same check, with the read its forms come from (null without forms), so its caller checks whose it is (decision 8).</summary>
    public static bool Authentic(AssistantObservedProposal proposal, [NotNullWhen(true)] out FormDocumentRowsProposal? payload,
        out FormObservedDocumentSelection? read)
    {
        payload = null;
        read = null;
        if (proposal.Id == Guid.Empty || proposal.TurnId == Guid.Empty
            || proposal.Capability is not { Id: BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, Version: BusinessFormDocumentAssistantCapabilitySource.Version }
            || proposal.Payload is not FormDocumentRowsProposal rows || !ValidProposal(rows)) return false;
        var latest = proposal.ObservedReads.Where(read => read.TurnId == proposal.TurnId && read.Capability.Id is BusinessFormDocumentAssistantCapabilitySource.CandidatesId
            or BusinessFormDocumentAssistantCapabilitySource.FormId).MaxBy(read => read.Ordinal);
        if (rows.Forms.Count != 0)
        {
            if (latest is null || latest.Ordinal != rows.ReadOrdinal || latest.Payload is not FormDocumentReadPayload observed
                || !FormDocumentCapabilityCodec.ValidRead(observed)
                || rows.Forms.Any(form => !observed.Forms.Any(candidate => candidate.DefinitionId == form.DefinitionId && candidate.VersionId == form.VersionId)))
                return false;
            read = new(latest.Capability.Id, new(latest.Ordinal, observed));
        }
        payload = rows;
        return true;
    }

    // Each sheet the sample shows, in its order: its rows of data (every row but its first, the headers; one without a visible cell is not
    // seen) and the columns any of its rows fills.
    private static Dictionary<string, (List<int> Rows, HashSet<string> Columns)> Sheets(StructuredSpreadsheet sample)
    {
        var sheets = new Dictionary<string, (List<int> Rows, HashSet<string> Columns)>(StringComparer.Ordinal);
        foreach (var row in sample.Rows)
        {
            var name = row.Sheet!;
            if (!sheets.TryGetValue(name, out var sheet)) sheets[name] = sheet = ([], new HashSet<string>(StringComparer.Ordinal));
            else if (row.Cells.Count > 0) sheet.Rows.Add(row.Number);
            sheet.Columns.UnionWith(row.Cells.Keys);
        }
        return sheets;
    }

    // The rows named for a sheet, in order: each a row of data the sample showed there and not named before.
    private static IReadOnlyList<int> Rows(string sheet, IReadOnlyList<int> rows, List<int> seen, string path, HashSet<(string, int)> named,
        List<FormDocumentEntryIssue> issues)
    {
        if (rows.Count == 0) issues.Add(new(path, FormDocumentEntryIssue.Count));
        var kept = new List<int>();
        for (var index = 0; index < rows.Count; index++)
        {
            if (!seen.Contains(rows[index])) issues.Add(new($"{path}[{index}]", UnknownRow));
            else if (!named.Add((sheet, rows[index]))) issues.Add(new($"{path}[{index}]", FormDocumentEntryIssue.Repeated));
            else kept.Add(rows[index]);
        }
        return [.. kept.Order()];
    }

    private static bool ValidSets(IReadOnlyList<FormDocumentRowSet> sets, bool allowEmpty) =>
        sets.Select(set => set.Sheet).Distinct(StringComparer.Ordinal).Count() == sets.Count
        && sets.All(set => ValidSheet(set.Sheet) && ValidRows(set.Rows, allowEmpty));

    private static bool ValidRows(IReadOnlyList<int>? rows, bool allowEmpty) => rows is { Count: <= MaxRows } && (allowEmpty || rows.Count > 0)
        && rows.All(row => row is >= 1 and <= MaxRowNumber) && rows.Zip(rows.Skip(1)).All(pair => pair.First < pair.Second);

    private static bool ValidSheet(string? sheet) => sheet is { Length: <= MaxSheetLength } && (sheet.Length == 0 || ArquitecturaBase.Domain.Common.StorableText.IsStorable(sheet));

    private static string Letter(string column) => column.Trim().ToUpperInvariant();

    private static bool ProposalShape(JsonElement value) => Has(value, "state", "readOrdinal", "forms", "unmatched", "seen", "documentType")
        && value.GetProperty("forms") is { ValueKind: JsonValueKind.Array } forms && forms.EnumerateArray().All(form =>
            Has(form, "definitionId", "versionId", "expectedDefinitionRevision", "sheet", "rows", "columns", "doubtful")
            && form.GetProperty("columns") is { ValueKind: JsonValueKind.Array } columns && columns.EnumerateArray().All(column => Has(column, "fieldId", "type", "column")))
        && SetsShape(value.GetProperty("unmatched")) && SetsShape(value.GetProperty("seen"));

    private static bool SetsShape(JsonElement sets) => sets.ValueKind == JsonValueKind.Array && sets.EnumerateArray().All(set => Has(set, "sheet", "rows"));

    private static bool Has(JsonElement value, params string[] properties) => FormAssistantCapabilityCodec.Has(value, properties);
    private static object Obj(params (string Name, object Schema)[] fields) => new
    { type = "object", properties = fields.ToDictionary(field => field.Name, field => field.Schema, StringComparer.Ordinal), required = fields.Select(field => field.Name).ToArray(), additionalProperties = false };
    private static object Arr(object items, int maximum) => new { type = "array", items, maxItems = maximum };
    private static object Str(params string[] values) => values.Length == 0 ? new { type = "string" } : new { type = "string", @enum = values };
}
