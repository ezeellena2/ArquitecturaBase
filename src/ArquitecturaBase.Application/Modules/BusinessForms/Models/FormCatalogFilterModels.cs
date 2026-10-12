using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogFilterCondition(string? Column, string? Operator, string? Value);

public sealed record FormCatalogFilterDefinition(
    string? TextColumn,
    string? DetailColumn,
    IReadOnlyList<FormCatalogFilterCondition?>? Conditions,
    bool ExcludeRecipient,
    string? SortColumn,
    bool SortDescending);

public sealed record FormCatalogFilterPreviewRequest(Guid SampleUserId, FormCatalogFilterDefinition? Filters);

public sealed record FormCatalogFilterOptionsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["name"];

}

public sealed record FormCatalogFilterSampleUserResponse(Guid Id, string Name);

public sealed record FormCatalogFilterChoiceResponse(string Key, string Label);

public sealed record FormCatalogFilterColumnResponse(
    string Key,
    string DataType,
    bool CanDisplay,
    bool CanFilter,
    bool CanSort,
    IReadOnlyList<string> Operators,
    IReadOnlyList<FormCatalogFilterChoiceResponse>? Choices);

public sealed record FormCatalogFilterOptionsResponse(
    string Source,
    IReadOnlyList<FormCatalogFilterColumnResponse> Columns,
    PagedResult<FormCatalogFilterSampleUserResponse>? SampleUsers,
    bool IsAvailable,
    string? UnavailableReasonCode);

public sealed record FormCatalogFilterOptionResponse(Guid Id, string Text, string? Detail);

public sealed record FormCatalogFilterPreviewResponse(
    IReadOnlyList<FormCatalogFilterOptionResponse> Items,
    int TotalCount,
    bool ExceedsLimit);

public sealed record CreateFiltersCatalogRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    FormCatalogFilterDefinition? Filters);

public sealed record UpdateFiltersCatalogRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    FormCatalogFilterDefinition? Filters);
