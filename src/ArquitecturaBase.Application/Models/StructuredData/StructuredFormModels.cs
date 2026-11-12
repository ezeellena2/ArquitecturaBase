using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormCheckRequest(int Format, string OriginalCulture, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> SubmitLabel, IReadOnlyList<StructuredFormQuestion> Questions, IReadOnlyList<string> TargetCultures,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? MaxFileSizeMiB = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StructuredFormSection>? Sections = null)
{
    public override string ToString() => "StructuredFormCheckRequest { Content = [REDACTED] }";
}
// A section title: Start is how many questions go before it; a channel shows it before its first question.
public sealed record StructuredFormSection(int Start, IReadOnlyDictionary<string, string> Title)
{
    public override string ToString() => "StructuredFormSection { Content = [REDACTED] }";
}
public sealed record StructuredFormQuestion(Guid FieldId, string Key, string Type, IReadOnlyDictionary<string, string> Label,
    IReadOnlyDictionary<string, string>? Help, bool Required, int? MaxLength, IReadOnlyList<StructuredFormChoice>? Options,
    Guid? SourceCatalogId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredFormFileSettings? Files = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredFormSelectionSettings? Selection = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredFormRowSettings? Rows = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? Repeated = null,
    // LD2-A: the question asks first for the value of a field of its catalog; the values and their options come with the offer.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? ChoosesFirst = null,
    // CA1-A: the owner computes the question when it receives the answers; a channel neither shows it nor sends an answer for it.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? Calculated = null,
    // CO1-A: the question shows only when an earlier question has one of these answers.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredFormShowWhen? ShowWhen = null,
    // W3 (UC1-A): the text question takes what the person shares on their own: "location", "contactName" or "contactPhone".
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Receives = null)
{
    public override string ToString() => "StructuredFormQuestion { Content = [REDACTED] }";
}
// CO1-A: the earlier question outside the block, by its FieldId, and the answers that show the question: option identities in
// the "D" format for choose one (own or from a catalog), "true" or "false" for yes or no.
public sealed record StructuredFormShowWhen(Guid FieldId, IReadOnlyList<string> Values)
{
    public override string ToString() => "StructuredFormShowWhen { Content = [REDACTED] }";
}
public sealed record StructuredFormChoice(Guid ChoiceId, IReadOnlyDictionary<string, string> Label)
{
    public override string ToString() => "StructuredFormChoice { Content = [REDACTED] }";
}
public sealed record StructuredFormIssueResponse(Guid? FieldId, Guid? ChoiceId, string Path, Error Error, int? Limit = null, string? Culture = null);
public sealed record StructuredFormCultureResponse(string Culture, string CompilationFingerprint, int ComponentCount);
public sealed record StructuredFormCheckResponse(string ProfileRevision, string Compatibility, string ExternalEvidence,
    IReadOnlyList<StructuredFormCultureResponse> Cultures, IReadOnlyList<StructuredFormIssueResponse> Issues, bool PublicationAvailable = false);

public sealed record StructuredFormSelectionSettings(int MinSelected, int MaxSelected);
// The block of rows is the "group" question; its questions follow it, marked Repeated.
public sealed record StructuredFormRowSettings(int MinRows, int MaxRows);
public sealed record StructuredFormFileSettings(int MinFiles, int MaxFiles, string? PhotoSource = null);
