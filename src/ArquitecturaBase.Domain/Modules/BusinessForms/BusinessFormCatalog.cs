using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormCatalog : Entity, IAuditable
{
    private BusinessFormCatalog()
    {
    }

    public string Key { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string DescriptionJson { get; private set; } = "{}";
    public BusinessFormCatalogKind Kind { get; private set; }
    public long Revision { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormCatalog CreateManual(IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        var catalog = new BusinessFormCatalog
        {
            NameJson = SerializeTexts(name),
            DescriptionJson = SerializeTexts(description),
            Kind = BusinessFormCatalogKind.Manual,
        };
        catalog.Key = catalog.Id.ToString("N");
        return catalog;
    }

    public Result UpdateManual(long expectedRevision, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description)
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

        NameJson = SerializeTexts(name);
        DescriptionJson = SerializeTexts(description);
        Revision++;
        return Result.Success();
    }

    public Result SetActive(long expectedRevision, bool isActive)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991)
        {
            return FormCatalogErrors.RevisionConflict;
        }

        if (IsActive != isActive)
        {
            IsActive = isActive;
            Revision++;
        }

        return Result.Success();
    }

    public Result<BusinessFormCatalogItem> AddManualItem(string code, string textEs, string? textEn,
        string? detailEs, string? detailEn, int position)
    {
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (!IsActive)
        {
            return FormCatalogErrors.Inactive;
        }

        var item = BusinessFormCatalogItem.Create(Id, code, textEs, textEn, detailEs, detailEn, position);
        return item;
    }

    public Result UpdateManualItem(BusinessFormCatalogItem item, string code, string textEs,
        string? textEn, string? detailEs, string? detailEn, int position)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (!IsActive)
        {
            return FormCatalogErrors.Inactive;
        }

        if (item.CatalogId != Id)
        {
            return FormCatalogErrors.ItemNotFound;
        }

        item.Update(code, textEs, textEn, detailEs, detailEn, position);
        return Result.Success();
    }

    public Result SetManualItemActive(BusinessFormCatalogItem item, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Kind != BusinessFormCatalogKind.Manual)
        {
            return FormCatalogErrors.ManualItemsUnavailable;
        }

        if (!IsActive)
        {
            return FormCatalogErrors.Inactive;
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

    private static string SerializeTexts(IReadOnlyDictionary<string, string> texts)
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in new[] { "es", "en" })
        {
            if (texts.TryGetValue(culture, out var text)) canonical[culture] = text;
        }

        return JsonSerializer.Serialize(canonical, JsonSerializerOptions.Web);
    }
}
