using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed partial class BusinessFormCatalog : Entity, IAuditable
{
    public const int MaxItems = 10_000;

    private BusinessFormCatalog()
    {
    }

    public string Key { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string DescriptionJson { get; private set; } = "{}";
    public string? FiltersJson { get; private set; }
    public BusinessFormCatalogKind Kind { get; private set; }
    public BusinessFormCatalogValueType ValueType { get; private set; } = BusinessFormCatalogValueType.Text;
    public string OriginalCulture { get; private set; } = "es";
    public long Revision { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormCatalog CreateManual(string originalCulture, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text)
    {
        ArgumentNullException.ThrowIfNull(description);
        RequireOriginalName(originalCulture, name);
        if (!Enum.IsDefined(valueType)) throw new ArgumentOutOfRangeException(nameof(valueType));
        var catalog = new BusinessFormCatalog
        {
            OriginalCulture = originalCulture,
            NameJson = SerializeTexts(name),
            DescriptionJson = SerializeTexts(description),
            Kind = BusinessFormCatalogKind.Manual,
            ValueType = valueType,
        };
        catalog.Key = catalog.Id.ToString("N");
        return catalog;
    }

    public static BusinessFormCatalog CreateFilters(string originalCulture, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, string filtersJson)
    {
        ArgumentNullException.ThrowIfNull(description);
        RequireOriginalName(originalCulture, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(filtersJson);
        var catalog = new BusinessFormCatalog
        {
            OriginalCulture = originalCulture,
            NameJson = SerializeTexts(name),
            DescriptionJson = SerializeTexts(description),
            FiltersJson = filtersJson,
            Kind = BusinessFormCatalogKind.Filters,
        };
        catalog.Key = catalog.Id.ToString("N");
        return catalog;
    }

    public Result UpdateManual(long expectedRevision, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, bool? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (Revision != expectedRevision || Revision >= 9007199254740991)
        {
            return FormCatalogErrors.RevisionConflict;
        }

        if (!HasOriginalName(name)) return FormCatalogErrors.ContentInvalid;
        NameJson = SerializeTexts(name);
        DescriptionJson = SerializeTexts(description);
        IsActive = isActive ?? IsActive;
        Revision++;
        return Result.Success();
    }

    public Result UpdateFilters(long expectedRevision, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, string filtersJson, bool? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(filtersJson);
        if (Kind != BusinessFormCatalogKind.Filters)
        {
            return FormCatalogErrors.FiltersUnavailable;
        }

        if (Revision != expectedRevision || Revision >= 9007199254740991)
        {
            return FormCatalogErrors.RevisionConflict;
        }

        if (isActive == true && ProposalState is BusinessFormCatalogProposalState.Pending or BusinessFormCatalogProposalState.Discarded)
            return FormCatalogErrors.Inactive;

        if (!HasOriginalName(name)) return FormCatalogErrors.ContentInvalid;
        NameJson = SerializeTexts(name);
        DescriptionJson = SerializeTexts(description);
        FiltersJson = filtersJson;
        IsActive = isActive ?? IsActive;
        Revision++;
        return Result.Success();
    }

    public Result SetActive(long expectedRevision, bool isActive)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991)
        {
            return FormCatalogErrors.RevisionConflict;
        }

        if (isActive && ProposalState is BusinessFormCatalogProposalState.Pending or BusinessFormCatalogProposalState.Discarded)
            return FormCatalogErrors.Inactive;

        if (IsActive != isActive)
        {
            IsActive = isActive;
            Revision++;
        }

        return Result.Success();
    }

    public Result<BusinessFormCatalogItem> AddManualItem(string code, string? textEs, string? textEn,
        string? detailEs, string? detailEn, int position, string? value = null)
    {
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (!HasOriginalText(textEs, textEn)) return FormCatalogErrors.ContentInvalid;
        var item = BusinessFormCatalogItem.Create(Id, code, textEs, textEn, detailEs, detailEn, position, value);
        return item;
    }

    public Result UpdateManualItem(BusinessFormCatalogItem item, string code, string? textEs,
        string? textEn, string? detailEs, string? detailEn, int position, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (item.CatalogId != Id)
        {
            return FormCatalogErrors.ItemNotFound;
        }

        if (!HasOriginalText(textEs, textEn)) return FormCatalogErrors.ContentInvalid;
        item.Update(code, textEs, textEn, detailEs, detailEn, position, value);
        return Result.Success();
    }

    public Result SetManualItemActive(BusinessFormCatalogItem item, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (item.CatalogId != Id)
        {
            return FormCatalogErrors.ItemNotFound;
        }

        if (item.IsActive != isActive)
        {
            item.SetActive(isActive);
        }

        return Result.Success();
    }

    private static void RequireOriginalName(string originalCulture, IReadOnlyDictionary<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (originalCulture is not ("es" or "en")) throw new ArgumentException("The original culture is invalid.", nameof(originalCulture));
        if (!name.TryGetValue(originalCulture, out var original) || string.IsNullOrWhiteSpace(original))
            throw new ArgumentException("The name is required in the original culture.", nameof(name));
    }

    private bool HasOriginalName(IReadOnlyDictionary<string, string> name) =>
        name.TryGetValue(OriginalCulture, out var original) && !string.IsNullOrWhiteSpace(original);

    // An item needs its text in the catalog's original culture; the other culture is an optional translation.
    private bool HasOriginalText(string? textEs, string? textEn) =>
        !string.IsNullOrWhiteSpace(OriginalCulture == "en" ? textEn : textEs);

    private static string SerializeTexts(IReadOnlyDictionary<string, string> texts)
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in new[] { "es", "en" })
        {
            if (texts.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text)) canonical[culture] = text;
        }

        return JsonSerializer.Serialize(canonical, JsonSerializerOptions.Web);
    }
}
