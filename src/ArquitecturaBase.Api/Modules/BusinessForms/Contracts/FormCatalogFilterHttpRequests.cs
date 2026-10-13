using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormCatalogFilterOptionsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogFilterConditionHttpRequest(string? Column, string? Operator, string? Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogFilterDefinitionHttpRequest(
    string? TextColumn,
    string? DetailColumn,
    IReadOnlyList<FormCatalogFilterConditionHttpRequest?>? Conditions,
    bool ExcludeRecipient,
    string? SortColumn,
    bool SortDescending);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogFilterPreviewHttpRequest(Guid SampleUserId, FormCatalogFilterDefinitionHttpRequest? Filters)
{
    public override string ToString() => "FormCatalogFilterPreviewHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFiltersCatalogHttpRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    FormCatalogFilterDefinitionHttpRequest? Filters)
{
    public override string ToString() => "CreateFiltersCatalogHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFiltersCatalogHttpRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    FormCatalogFilterDefinitionHttpRequest? Filters, bool? IsActive = null)
{
    public override string ToString() => "UpdateFiltersCatalogHttpRequest { Content = [REDACTED] }";
}
