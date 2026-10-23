using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateManualCatalogHttpRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemHttpRequest?>? Items, string? ValueType = null, IReadOnlyList<FormCatalogFieldHttpRequest?>? Fields = null, FormCatalogPresentationHttpRequest? Presentation = null)
{
    public override string ToString() => "CreateManualCatalogHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateManualCatalogHttpRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemHttpRequest?>? Items,
    IReadOnlyList<FormCatalogManualItemMoveHttpRequest?>? Moves = null, bool? IsActive = null, bool SortAlphabetically = false, IReadOnlyList<FormCatalogFieldHttpRequest?>? Fields = null,
    FormCatalogPresentationHttpRequest? Presentation = null, IReadOnlyList<Guid>? ConfirmFieldRemovals = null)
{
    public override string ToString() => "UpdateManualCatalogHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogManualItemMoveHttpRequest(Guid? ItemId, string? Direction);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogManualItemHttpRequest(
    string? LocalId,
    Guid? ItemId,
    string? Code,
    IReadOnlyDictionary<string, string?>? Text,
    IReadOnlyDictionary<string, string?>? Detail,
    bool IsActive, string? Value = null, IReadOnlyDictionary<string, string?>? Fields = null)
{
    public override string ToString() => "FormCatalogManualItemHttpRequest { Content = [REDACTED] }";
}

public sealed record FormCatalogsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null,
    string? Kind = null, string? State = null);

public sealed record FormCatalogItemsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null, bool? IsActive = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogFieldHttpRequest(string? LocalId, Guid? Id, string? Key,
    IReadOnlyDictionary<string, string?>? Name, string? Type, Guid? SourceCatalogId, bool Required,
    bool Unique, string? Min, string? Max, int? MaxLength, string? Role, int Position, bool IsActive)
{
    public override string ToString() => "FormCatalogFieldHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogPresentationHttpRequest(IReadOnlyList<string>? Title, IReadOnlyList<string>? Description);
