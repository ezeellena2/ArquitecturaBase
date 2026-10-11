using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record ListStructuredFormsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = Array.AsReadOnly<string>(["name"]);
    public override string ToString() => "ListStructuredFormsRequest { Search = [REDACTED] }";
}

public sealed record StructuredFormSelectionRow(Guid DefinitionId, Guid VersionId, long VersionNumber, string Prefix,
    IReadOnlyDictionary<string, string> Name, int QuestionCount, IReadOnlyDictionary<string, string> SubmitLabel)
{
    public override string ToString() => "StructuredFormSelectionRow { Presentation = [REDACTED] }";
}
