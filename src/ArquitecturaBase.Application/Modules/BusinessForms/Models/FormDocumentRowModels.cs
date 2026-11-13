using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// VC1-A, task 27: a row of data of a spreadsheet to load, as the copy has it: its sheet (named as the sample to load names it), its number,
// the headers of its sheet (its first row with content) and its cells, both by column.
public sealed record FormDocumentSheetRow(string Sheet, int Number, IReadOnlyList<FormSpreadsheetCell> Headers, IReadOnlyList<FormSpreadsheetCell> Cells)
{
    public override string ToString() => "FormDocumentSheetRow { Content = [REDACTED] }";
}

// forms.documents.propose-rows@1 (decision 9): for each form the model recognizes in a sheet, its rows, which column goes to each question
// and which columns it read with doubts; apart, the rows of data that fit no form, and the type of document in a few generic words.
internal sealed record FormDocumentRowsArguments(IReadOnlyList<FormDocumentRowFormArgument> Forms, IReadOnlyList<FormDocumentRowSet> Unmatched,
    string DocumentType)
{
    public override string ToString() => "FormDocumentRowsArguments { Content = [REDACTED] }";
}
internal sealed record FormDocumentRowFormArgument(Guid DefinitionId, Guid VersionId, string Sheet, IReadOnlyList<int> Rows,
    IReadOnlyList<FormDocumentRowColumnArgument> Columns, IReadOnlyList<string> Doubtful)
{
    public override string ToString() => "FormDocumentRowFormArgument { Content = [REDACTED] }";
}
internal sealed record FormDocumentRowColumnArgument(Guid FieldId, string Column)
{
    public override string ToString() => "FormDocumentRowColumnArgument { Content = [REDACTED] }";
}

// The proposal as its owner normalized it against the latest documents read of the turn and the sample to load: each form with the revision
// it was read with, its rows in order, each column in capitals with the type of its question, the rows that fit no form and, set by the
// server from the sample and never by the model, the rows of data the model saw: one it did not see stays «Sin clasificar».
public sealed record FormDocumentRowsProposal(string State, int ReadOrdinal, IReadOnlyList<FormDocumentRowForm> Forms,
    IReadOnlyList<FormDocumentRowSet> Unmatched, IReadOnlyList<FormDocumentRowSet> Seen, string DocumentType) : AssistantCapabilityPayload;
public sealed record FormDocumentRowForm(Guid DefinitionId, Guid VersionId, long ExpectedDefinitionRevision, string Sheet, IReadOnlyList<int> Rows,
    IReadOnlyList<FormDocumentRowColumn> Columns, IReadOnlyList<string> Doubtful)
{
    public override string ToString() => "FormDocumentRowForm { Content = [REDACTED] }";
}
public sealed record FormDocumentRowColumn(Guid FieldId, string Type, string Column)
{
    public override string ToString() => "FormDocumentRowColumn { Content = [REDACTED] }";
}
public sealed record FormDocumentRowSet(string Sheet, IReadOnlyList<int> Rows)
{
    public override string ToString() => "FormDocumentRowSet { Content = [REDACTED] }";
}

/// <summary>
/// What became of a row of data (VC1-A): one of the first three loads in the order of the spreadsheet, a load over that limit (for the team),
/// a repeated one (of a response with the same data, or equal to an earlier row), one that fits no form, or one the model did not see.
/// </summary>
public enum FormDocumentRowOutcome { Load, OverLimit, Repeated, Unmatched, Unseen }

/// <summary>
/// A row of data with what became of it: the form it was mapped to, prepared like a document's candidate (a load, one over the limit or a
/// repeated one), and where a repeated one already is: the response with the same data, the earlier row it is equal to, or both.
/// </summary>
public sealed record FormDocumentPreparedRow(string Sheet, int Number, FormDocumentRowOutcome Outcome)
{
    public FormDocumentPreparedCandidate? Candidate { get; init; }
    public FormDocumentRepetition? Repetition { get; init; }
    public FormDocumentRowReference? SameAs { get; init; }
    public override string ToString() => $"FormDocumentPreparedRow {{ Outcome = {Outcome}, Content = [REDACTED] }}";
}
public sealed record FormDocumentRowReference(string Sheet, int Number)
{
    public override string ToString() => "FormDocumentRowReference { Content = [REDACTED] }";
}

/// <summary>The rows of data of a spreadsheet in its order, each with what became of it, and the type of document the model named.</summary>
public sealed record FormDocumentRowPlan(IReadOnlyList<FormDocumentPreparedRow> Rows, string DocumentType)
{
    /// <summary>Whether some row fits a form; when none does, the spreadsheet follows «No lo reconoce» (DOC2).</summary>
    public bool AnyFits => Rows.Any(row => row.Candidate is not null);
    public override string ToString() => "FormDocumentRowPlan { Content = [REDACTED] }";
}
