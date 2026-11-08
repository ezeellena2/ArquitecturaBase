using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormCatalogOptionText(string Text, string? Detail)
{
    public override string ToString() => "StructuredFormCatalogOptionText { Content = [REDACTED] }";
}

/// <summary>
/// An offered option. <see cref="Group"/> (LD2-A) is its value in the field the question chooses first by, as
/// <see cref="StructuredFormCatalogGroup.Value"/> gives it; in a grouped offer, null puts it with the options without a value.
/// </summary>
public sealed record StructuredFormCatalogOption(Guid Id, string Text, string? Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, StructuredFormCatalogOptionText>? Translations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredFormCatalogShown? Shown = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Group = null)
{
    public override string ToString() => "StructuredFormCatalogOption { Content = [REDACTED] }";
}

/// <summary>LD2-A: the values of the field a question chooses first by, in order, and the field's name in the offer's culture.</summary>
public sealed record StructuredFormCatalogGrouping(string Label, IReadOnlyList<StructuredFormCatalogGroup> Groups)
{
    public override string ToString() => "StructuredFormCatalogGrouping { Content = [REDACTED] }";
}

/// <summary>A value and its text; a null <see cref="Value"/> groups the options without one («Sin rubro»).</summary>
public sealed record StructuredFormCatalogGroup(string? Value, string Text)
{
    public override string ToString() => "StructuredFormCatalogGroup { Content = [REDACTED] }";
}

public sealed record StructuredFormCatalogShown(string Title, string? Description)
{
    public override string ToString() => "StructuredFormCatalogShown { Content = [REDACTED] }";
}

public sealed record StructuredFormCatalogOffer(Guid FieldId, string Key, string Label, Guid CatalogId,
    long Revision, string Kind, string CatalogName, IReadOnlyList<StructuredFormCatalogOption> Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? CatalogNames = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredFormCatalogGrouping? Grouping = null)
{
    public override string ToString() => "StructuredFormCatalogOffer { Content = [REDACTED] }";
}

public sealed record StructuredFormPreparationIssue(Guid FieldId, string Label, string Code,
    string? CatalogName = null, int? InvalidOptionCount = null, string? MissingData = null);
public sealed record StructuredFormPreparationResponse(IReadOnlyList<StructuredFormCatalogOffer> Catalogs,
    IReadOnlyList<StructuredFormPreparationIssue> Issues)
{
    public string? Culture { get; init; }
    public bool CanSend => Issues.Count == 0;
    public override string ToString() => "StructuredFormPreparationResponse { Content = [REDACTED] }";
}
