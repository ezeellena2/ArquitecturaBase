namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>A result as it is stored (RE1-A); <c>FormRecordResultRecorder.Read</c> opens its JSON.</summary>
public sealed class FormRecordResultRow
{
    public Guid RecordId { get; init; }
    public Guid RunId { get; init; }
    public string Source { get; init; } = "";
    public string SourceNameJson { get; init; } = "{}";
    public string ValuesJson { get; init; } = "[]";
    public DateTime RecordedAtUtc { get; init; }
    public override string ToString() => "FormRecordResultRow { Content = [REDACTED] }";
}

/// <summary>What an external action returned, in the detail of a response (RE1-A): a zone above «Datos» named after the action.</summary>
public sealed record FormResponseResultResponse(string Source, string Name, DateTime RecordedAtUtc, IReadOnlyList<FormResponseResultValueResponse> Values)
{
    public override string ToString() => "FormResponseResultResponse { Content = [REDACTED] }";
}
/// <summary>A value of a result: its name in the display language, its kind (text, date or number) and its canonical value.</summary>
public sealed record FormResponseResultValueResponse(string Key, string Label, string Kind, string? Value)
{
    public override string ToString() => "FormResponseResultValueResponse { Content = [REDACTED] }";
}

/// <summary>
/// A column of the sheet and the Excel for a datum of a result (RE1-A), after the questions: its id is «&lt;source&gt;.&lt;key&gt;»,
/// and its names come from the latest result of its source among the form's responses.
/// </summary>
public sealed record FormResponseResultColumn(string Id, string Source, string SourceName, string Key, string Label, string Kind);
