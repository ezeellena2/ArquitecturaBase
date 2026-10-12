using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateManualCatalogHttpRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemHttpRequest?>? Items)
{
    public override string ToString() => "CreateManualCatalogHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateManualCatalogHttpRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemHttpRequest?>? Items,
    IReadOnlyList<FormCatalogManualItemMoveHttpRequest?>? Moves = null)
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
    bool IsActive)
{
    public override string ToString() => "FormCatalogManualItemHttpRequest { Content = [REDACTED] }";
}

public sealed record FormCatalogsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null,
    string? Kind = null, string? State = null);

public sealed record FormCatalogItemsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null, bool? IsActive = null);
