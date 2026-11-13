using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The rows of a spreadsheet to load (decision 9, VC1-A). In each sheet the rows with content (<see cref="FormSpreadsheetContentPolicy"/>) are
/// its first one, the headers, and its rows of data, each sheet named as the samples name it: the one rule of the sample the model sees and of
/// the copy formularios loads from. With the mapping of forms.documents.propose-rows@1 each row of data is armed from the copy, never from the
/// sample, without AI and in the order of the spreadsheet: its cells converted to the type of their questions (as a text the import reads, with
/// the conventions of the person's language; an option by its text), validated and completed like a document's candidate, repeated when a
/// response has its data (the standard's duplicate keys) or an earlier row of its form is equal to it, and a load while there are fewer than
/// <see cref="MaximumLoads"/>; with the forms its caller knows its person can receive (CE4-A), one of another form is for the team and does
/// not count, since the limit counts the loads proposed to the person. A row that fits no form, of a form that changed its version or stopped being active, or one the model did not
/// see, fits none; one the model saw and did not name (a total, a note) is not a row.
/// </summary>
internal sealed class FormDocumentRowPolicy(IFormReceivedDocumentReader reader, FormDocumentProposalPolicy proposals)
{
    /// <summary>The loads proposed for one spreadsheet, the limit of actions of the assistant's cases.</summary>
    public const int MaximumLoads = 3;

    // The sheets the person sees, never one the workbook hides (the list of a dropdown), with their rows with content.
    public static IReadOnlyList<(string Name, IReadOnlyList<FormSpreadsheetRow> Rows)> Sheets(IReadOnlyList<FormSpreadsheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        return [.. sheets.Where(sheet => !sheet.Hidden).Select(sheet => (FormAttachmentPreparationPolicy.SheetName(sheet.Name),
            (IReadOnlyList<FormSpreadsheetRow>)[.. sheet.Rows.Where(FormSpreadsheetContentPolicy.HasContent)]))];
    }

    /// <summary>The rows of data of every sheet, in the order of the spreadsheet, each with its sheet, its number and the headers of its sheet.</summary>
    public static IReadOnlyList<FormDocumentSheetRow> DataRows(IReadOnlyList<FormSpreadsheet> sheets) =>
        [.. Sheets(sheets).Where(sheet => sheet.Rows.Count > 1)
            .SelectMany(sheet => sheet.Rows.Skip(1).Select(row => new FormDocumentSheetRow(sheet.Name, row.Number, sheet.Rows[0].Cells, row.Cells)))];

    /// <summary>The summary of a load: its first two data, in the order of the form (decision 9).</summary>
    public static IReadOnlyList<FormDocumentPreparedValue> Summary(FormDocumentPreparedCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return [.. candidate.Values.Where(value => value.Value is not null).Take(2)];
    }

    public async Task<FormDocumentRowPlan> PrepareAsync(FormDocumentRowsProposal proposal, IReadOnlyList<FormDocumentSheetRow> rows, string culture,
        CancellationToken ct, IReadOnlySet<Guid>? sendable = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(rows);
        var seen = proposal.Seen.SelectMany(set => set.Rows.Select(number => (set.Sheet, number))).ToHashSet();
        var unmatched = proposal.Unmatched.SelectMany(set => set.Rows.Select(number => (set.Sheet, number))).ToHashSet();
        var mapped = new Dictionary<(string, int), FormDocumentRowForm>();
        foreach (var form in proposal.Forms)
            foreach (var number in form.Rows) mapped.TryAdd((form.Sheet, number), form);
        var forms = new Dictionary<Guid, (FormDocumentFormRow Row, FormVersionSchema Schema)?>();
        var armed = new List<(FormDocumentSheetRow Row, Guid DefinitionId, FormDocumentPreparedRow Result)>();
        var results = new List<FormDocumentPreparedRow>();
        var loads = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var key = (row.Sheet, row.Number);
            if (!seen.Contains(key))
            {
                results.Add(new(row.Sheet, row.Number, FormDocumentRowOutcome.Unseen));
                continue;
            }
            if (!mapped.TryGetValue(key, out var mapping))
            {
                // A row the model saw and did not name is not a row of data: a total or a note.
                if (unmatched.Contains(key)) results.Add(new(row.Sheet, row.Number, FormDocumentRowOutcome.Unmatched));
                continue;
            }
            if (!forms.TryGetValue(mapping.DefinitionId, out var active))
                forms[mapping.DefinitionId] = active = await reader.FindActiveFormAsync(mapping.DefinitionId, ct) is { } found && found.VersionId == mapping.VersionId
                    ? (found, FormDraftPolicy.Read<FormVersionSchema>(found.SchemaJson)) : null;
            if (active is not { } current)
            {
                results.Add(new(row.Sheet, row.Number, FormDocumentRowOutcome.Unmatched));
                continue;
            }
            var (form, schema) = current;
            FormDocumentPreparedRow result;
            var equal = armed.FirstOrDefault(previous => previous.DefinitionId == mapping.DefinitionId && Equal(previous.Row, row));
            if (equal.Row is not null)
                result = new(row.Sheet, row.Number, FormDocumentRowOutcome.Repeated)
                {
                    Candidate = equal.Result.Candidate, SameAs = new(equal.Row.Sheet, equal.Row.Number), Repetition = equal.Result.Repetition,
                };
            else
            {
                var candidate = await proposals.PrepareCandidateAsync(form, schema, Candidate(form, schema, mapping, row, culture), ct);
                var repetition = await proposals.FindRepeatedDataAsync(form, candidate, ct);
                var outcome = repetition is not null ? FormDocumentRowOutcome.Repeated
                    : sendable is not null && !sendable.Contains(mapping.DefinitionId) ? FormDocumentRowOutcome.NotSendable
                    : ++loads <= MaximumLoads ? FormDocumentRowOutcome.Load : FormDocumentRowOutcome.OverLimit;
                result = new(row.Sheet, row.Number, outcome) { Candidate = candidate, Repetition = repetition };
            }
            armed.Add((row, mapping.DefinitionId, result));
            results.Add(result);
        }
        return new FormDocumentRowPlan(results, proposal.DocumentType);
    }

    // The values of a row for its form: each mapped question with its cell converted, and doubtful when the model read its column with doubts.
    private static FormDocumentEntryCandidate Candidate(FormDocumentFormRow form, FormVersionSchema schema, FormDocumentRowForm mapping, FormDocumentSheetRow row,
        string culture)
    {
        var questions = FormDocumentFormPolicy.ReadQuestions(schema).ToDictionary(question => question.FieldId);
        var values = new List<FormDocumentEntryValue>();
        foreach (var column in mapping.Columns)
        {
            if (!questions.TryGetValue(column.FieldId, out var question)) continue;
            var index = FormAuthoringSpreadsheetPolicy.Number(column.Column) - 1;
            var cell = index is { } position && position >= 0 && position < row.Cells.Count ? row.Cells[position] : null;
            values.Add(new(question.FieldId, question.Type, cell is null ? null : Convert(question, cell, culture), mapping.Doubtful.Contains(column.Column)));
        }
        return new(form.DefinitionId, form.VersionId, mapping.ExpectedDefinitionRevision, values);
    }

    // A cell as its question takes it, or null when it does not convert. The reader's numbers and dates are canonical already, and a date with
    // its time is never cut to the day; a text is read as the import reads it, with the conventions of the person's language («150.000,50»,
    // «03/11/2026»), an ambiguous amount is not read, yes or no as the import takes them and an option by its text in either language.
    private static string? Convert(CanonicalFormField question, FormSpreadsheetCell cell, string culture)
    {
        if (cell.Error is not null || cell.Text?.Trim() is not { Length: > 0 } text) return null;
        switch (question.Type)
        {
            case "shortText":
            case "longText":
                return text;
            case "decimal":
            case "date":
                var type = question.Type == "decimal" ? BusinessFormCatalogValueType.Decimal : BusinessFormCatalogValueType.Date;
                var value = FormCatalogValuePolicy.NormalizeImport(type, text, culture, cell.IsCanonicalValue);
                return value.IsSuccess ? value.Value : null;
            case "boolean":
                return text.ToLowerInvariant() switch { "true" or "sí" or "si" or "yes" or "1" => "true", "false" or "no" or "0" => "false", _ => null };
            case "choice":
                var options = question.Options?.Where(option => option.Label.Values.Any(label => string.Equals(label.Trim(), text, StringComparison.OrdinalIgnoreCase)))
                    .ToArray() ?? [];
                return options.Length == 1 ? options[0].ChoiceId.ToString("D") : null;
            default:
                return null;
        }
    }

    // Two rows are equal when every cell is, its text without the spaces around it; an empty cell at the end changes nothing.
    private static bool Equal(FormDocumentSheetRow first, FormDocumentSheetRow second) => Cells(first).SequenceEqual(Cells(second));

    private static List<string?> Cells(FormDocumentSheetRow row)
    {
        var cells = row.Cells.Select(cell => cell.Error is not null ? "\0" : string.IsNullOrWhiteSpace(cell.Text) ? null : cell.Text.Trim()).ToList();
        while (cells.Count > 0 && cells[^1] is null) cells.RemoveAt(cells.Count - 1);
        return cells;
    }
}
