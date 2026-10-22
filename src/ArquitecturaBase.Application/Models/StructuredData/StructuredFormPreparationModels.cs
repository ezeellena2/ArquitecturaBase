using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormCatalogOptionText(string Text, string? Detail)
{
    public override string ToString() => "StructuredFormCatalogOptionText { Content = [REDACTED] }";
}

public sealed record StructuredFormCatalogOption(Guid Id, string Text, string? Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, StructuredFormCatalogOptionText>? Translations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null)
{
    public override string ToString() => "StructuredFormCatalogOption { Content = [REDACTED] }";
}

public sealed record StructuredFormCatalogOffer(Guid FieldId, string Key, string Label, Guid CatalogId,
    long Revision, string Kind, string CatalogName, IReadOnlyList<StructuredFormCatalogOption> Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? CatalogNames = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueType = null)
{
    public override string ToString() => "StructuredFormCatalogOffer { Content = [REDACTED] }";
}

public sealed record StructuredFormPreparationIssue(Guid FieldId, string Label, string Code,
    string? CatalogName = null, int? InvalidOptionCount = null);
public sealed record StructuredFormPreparationResponse(IReadOnlyList<StructuredFormCatalogOffer> Catalogs,
    IReadOnlyList<StructuredFormPreparationIssue> Issues)
{
    public string? Culture { get; init; }
    public bool CanSend => Issues.Count == 0;
    public override string ToString() => "StructuredFormPreparationResponse { Content = [REDACTED] }";
}
