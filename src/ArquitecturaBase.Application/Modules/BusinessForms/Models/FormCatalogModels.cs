using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record ListFormCatalogsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "kind", "revision", "optionsCount", "usageCount"];

    public string? Kind { get; init; }
    public string? State { get; init; }
    public string DisplayCulture { get; init; } = "es";
    // CP1-A: a folder with its subfolders, or the items without a folder (WithoutFolder).
    public Guid? FolderId { get; init; }
    public bool WithoutFolder { get; init; }
}

public sealed record ListFormCatalogItemsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["position", "code", "text", "isActive"];

    public bool? IsActive { get; init; }
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public string DisplayCulture { get; init; } = "es";
}

/// <summary>
/// Only modified rows are sent. ItemId distinguishes saved rows; LocalId correlates unsaved rows.
/// Fields is a delta: an absent key keeps the stored value and null clears it.
/// </summary>
public sealed record FormCatalogManualItemSaveRequest(
    string? LocalId,
    Guid? ItemId,
    string? Code,
    IReadOnlyDictionary<string, string?>? Text,
    IReadOnlyDictionary<string, string?>? Detail,
    bool IsActive, string? Value = null, IReadOnlyDictionary<string, string?>? Fields = null);

public sealed record CreateManualCatalogRequest(
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemSaveRequest?>? Items, string? ValueType = null,
    IReadOnlyList<FormCatalogFieldSaveRequest?>? Fields = null, FormCatalogPresentation? Presentation = null);

public sealed record UpdateManualCatalogRequest(
    long? ExpectedRevision,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyDictionary<string, string?>? Description,
    IReadOnlyList<FormCatalogManualItemSaveRequest?>? Items,
    IReadOnlyList<FormCatalogManualItemMoveRequest?>? Moves = null, bool? IsActive = null, FormCatalogItemSortRequest? Sort = null, IReadOnlyList<FormCatalogFieldSaveRequest?>? Fields = null,
    FormCatalogPresentation? Presentation = null, IReadOnlyList<Guid>? ConfirmFieldRemovals = null, IReadOnlyList<Guid>? DeleteItemIds = null);

public sealed record FormCatalogManualItemMoveRequest(Guid? ItemId, string? Direction);

/// <summary>
/// Orders every option of the catalog when saving. Column is "text", "code" or the id of an active field; options that tie keep
/// their current order and those without a value go last in both directions.
/// </summary>
public sealed record FormCatalogItemSortRequest(string? Column, bool Descending = false);

public sealed record FormCatalogListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string Origin,
    int? OptionsCount,
    int? UsageCount,
    string State,
    string? StateReason,
    bool CanEdit, string ValueType = "Text");

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

public sealed record FormCatalogOriginCounts(int All, int Manual, int Filters)
{
    /// <summary>Catalogs an installed module keeps; written only when there is one.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int External { get; init; }
}

public sealed record FormCatalogDetailResponse(
    Guid Id,
    string Key,
    IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description,
    string OriginalCulture,
    string Kind,
    long Revision,
    bool IsActive,
    int? ItemsCount,
    int? UsageCount,
    string? UnavailableReasonCode,
    FormCatalogFilterDefinition? Filters = null, FormCatalogProposalResponse? Proposal = null, string ValueType = "Text",
    IReadOnlyList<FormCatalogFieldResponse>? Fields = null, FormCatalogPresentation? Presentation = null,
    FormCatalogPresentationCheckResponse? PresentationCheck = null)
{
    /// <summary>When the source of an External catalog last answered or failed, and why; absent in the others.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormCatalogFeedResponse? Feed { get; init; }
}

public sealed record FormCatalogFeedResponse(DateTime? RefreshedAtUtc, DateTime? FailedAtUtc, string? FailureCode);

public sealed record FormCatalogItemResponse(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string?> Text,
    IReadOnlyDictionary<string, string?> Detail,
    int Position,
    bool IsActive, string? Value,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FieldTexts);

public sealed record FormCatalogSaveResponse(
    FormCatalogDetailResponse Catalog,
    IReadOnlyDictionary<string, Guid> ItemIdentityMap, IReadOnlyDictionary<string, Guid>? FieldIdentityMap = null);

public sealed record FormCatalogStoredRow(
    Guid Id,
    string Key,
    string NameJson,
    string DescriptionJson,
    string OriginalCulture,
    string Kind,
    long Revision,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy,
    int? ItemsCount,
    string? FiltersJson = null,
    string? ProposalEvidenceJson = null, BusinessFormCatalogProposalState? ProposalState = null,
    Guid? ProposalDecidedByUserId = null, DateTime? ProposalDecidedAtUtc = null, string ValueType = "Text",
    string? LastImportSha256 = null, DateTime? LastImportAtUtc = null, string? FieldsJson = null, string? PresentationJson = null,
    Guid? ProposalId = null)
{
    public DateTime? FeedRefreshedAtUtc { get; init; }
    public DateTime? FeedFailedAtUtc { get; init; }
    public string? FeedFailureCode { get; init; }
}

public sealed record FormCatalogStoredItemRow(
    Guid Id,
    Guid CatalogId,
    string Code,
    string? TextEs,
    string? TextEn,
    string? DetailEs,
    string? DetailEn,
    int Position,
    bool IsActive, string? Value = null, string FieldValuesJson = "{}")
{
    // The text written in a culture, or null when the option has no text in it.
    public string? TextIn(string culture) => Present(culture == "en" ? TextEn : TextEs);

    // The text in the requested culture or, without a translation, in the other one: a stored option always has its original.
    public string TextFor(string culture) => TextIn(culture) ?? TextIn(culture == "en" ? "es" : "en") ?? "";

    public string? DetailIn(string culture) => Present(culture == "en" ? DetailEn : DetailEs);

    public string? DetailFor(string culture) => DetailIn(culture) ?? DetailIn(culture == "en" ? "es" : "en");

    private static string? Present(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public sealed record FormCatalogMaximumOfferRow(int Maximum, string ValuesJson, int? Groups = null, int? LargestGroup = null)
{ public override string ToString() => "FormCatalogMaximumOfferRow { Content = [REDACTED] }"; }
