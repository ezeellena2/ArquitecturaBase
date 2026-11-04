using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record ListStructuredFormsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = Array.AsReadOnly<string>(["name"]);
    public override string ToString() => "ListStructuredFormsRequest { Search = [REDACTED] }";
}

/// <summary>
/// A form a channel can send. <see cref="DependentQuestion"/> is the label of its first question whose options depend on another
/// answer: a channel cannot ask it, so the form is loaded only in the app.
/// </summary>
public sealed record StructuredFormSelectionRow(Guid DefinitionId, Guid VersionId, long VersionNumber, string Prefix,
    IReadOnlyDictionary<string, string> Name, int QuestionCount, IReadOnlyDictionary<string, string> SubmitLabel,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool HasCatalogQuestions = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? DependentQuestion = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long DefinitionRevision { get; init; }
    public override string ToString() => "StructuredFormSelectionRow { Presentation = [REDACTED] }";
}
