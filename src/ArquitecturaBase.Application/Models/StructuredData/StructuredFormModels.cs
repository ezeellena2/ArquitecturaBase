using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormCheckRequest(int Format, string OriginalCulture, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> SubmitLabel, IReadOnlyList<StructuredFormQuestion> Questions, IReadOnlyList<string> TargetCultures)
{
    public override string ToString() => "StructuredFormCheckRequest { Content = [REDACTED] }";
}
public sealed record StructuredFormQuestion(Guid FieldId, string Key, string Type, IReadOnlyDictionary<string, string> Label,
    IReadOnlyDictionary<string, string>? Help, bool Required, int? MaxLength, IReadOnlyList<StructuredFormChoice>? Options,
    Guid? SourceCatalogId = null)
{
    public override string ToString() => "StructuredFormQuestion { Content = [REDACTED] }";
}
public sealed record StructuredFormChoice(Guid ChoiceId, IReadOnlyDictionary<string, string> Label)
{
    public override string ToString() => "StructuredFormChoice { Content = [REDACTED] }";
}
public sealed record StructuredFormIssueResponse(Guid? FieldId, Guid? ChoiceId, string Path, Error Error, int? Limit = null, string? Culture = null);
public sealed record StructuredFormCultureResponse(string Culture, string CompilationFingerprint, int ComponentCount);
public sealed record StructuredFormCheckResponse(string ProfileRevision, string Compatibility, string ExternalEvidence,
    IReadOnlyList<StructuredFormCultureResponse> Cultures, IReadOnlyList<StructuredFormIssueResponse> Issues, bool PublicationAvailable = false);
