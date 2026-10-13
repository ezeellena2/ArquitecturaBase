using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record ListFormCatalogsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "kind", "revision", "optionsCount", "usageCount"];

    public string? Kind { get; init; }
    public string? State { get; init; }
    public string DisplayCulture { get; init; } = "es";
}

public sealed record ListFormCatalogItemsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["position", "code", "text", "isActive"];

    public bool? IsActive { get; init; }
    public string DisplayCulture { get; init; } = "es";
}

/// <summary>Only modified rows are sent. ItemId distinguishes saved rows; LocalId correlates unsaved rows.</summary>
public sealed record FormCatalogManualItemSaveRequest(
    string? LocalId,
    Guid? ItemId,
    string? Code,
    IReadOnlyDictionary<string, string?>? Text,
    IReadOnlyDictionary<string, string?>? Detail,
    bool IsActive);

public sealed record CreateManualCatalogRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemSaveRequest?>? Items);

public sealed record UpdateManualCatalogRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemSaveRequest?>? Items,
    IReadOnlyList<FormCatalogManualItemMoveRequest?>? Moves = null, bool? IsActive = null);

public sealed record FormCatalogManualItemMoveRequest(Guid? ItemId, string? Direction);

public sealed record FormCatalogListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string Origin,
    int? OptionsCount,
    int? UsageCount,
    string State,
    string? StateReason,
    bool CanEdit);

public sealed record FormCatalogsPageResponse(
    IReadOnlyList<FormCatalogListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    FormCatalogFilterCounts FilterCounts)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

public sealed record FormCatalogFilterCounts(FormCatalogOriginCounts ByKind);

public sealed record FormCatalogOriginCounts(int All, int Manual, int Filters);

public sealed record FormCatalogDetailResponse(
    Guid Id,
    string Key,
    IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description,
    string Kind,
    long Revision,
    bool IsActive,
    int? ItemsCount,
    int? UsageCount,
    string? UnavailableReasonCode,
    FormCatalogFilterDefinition? Filters = null);

public sealed record FormCatalogItemResponse(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string?> Text,
    IReadOnlyDictionary<string, string?> Detail,
    int Position,
    bool IsActive);

public sealed record FormCatalogSaveResponse(
    FormCatalogDetailResponse Catalog,
    IReadOnlyDictionary<string, Guid> ItemIdentityMap);

public sealed record FormCatalogStoredRow(
    Guid Id,
    string Key,
    string NameJson,
    string DescriptionJson,
    string Kind,
    long Revision,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy,
    int? ItemsCount,
    string? FiltersJson = null);

public sealed record FormCatalogStoredItemRow(
    Guid Id,
    Guid CatalogId,
    string Code,
    string TextEs,
    string? TextEn,
    string? DetailEs,
    string? DetailEn,
    int Position,
    bool IsActive);
