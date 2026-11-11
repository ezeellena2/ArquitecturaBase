using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>An item as its source sends it: a stable code, its texts and its typed value in canonical form.</summary>
public sealed record BusinessFormFeedItem(string Code, string? TextEs, string? TextEn, string? Value);

// A catalog an installed module feeds (External): a read-only local copy that only its source changes. Reading the source has
// no effect outside the system (ADR 0018): a short reservation keeps a second host from asking at the same time, and the
// differences are applied with the catalog revision. What stops coming is deactivated, never deleted: responses keep naming it.
public sealed partial class BusinessFormCatalog
{
    public DateTime? FeedRefreshedAtUtc { get; private set; }
    public DateTime? FeedFailedAtUtc { get; private set; }
    public string? FeedFailureCode { get; private set; }
    public DateTime? FeedLeaseUntilUtc { get; private set; }

    public static BusinessFormCatalog CreateExternal(string key, string originalCulture, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, BusinessFormCatalogValueType valueType)
    {
        var catalog = CreateManual(originalCulture, name, description, valueType);
        catalog.Kind = BusinessFormCatalogKind.External;
        catalog.AssignStandardKey(key);
        return catalog;
    }

    public bool TryReserveFeed(DateTime nowUtc, TimeSpan lease)
    {
        if (Kind != BusinessFormCatalogKind.External || FeedLeaseUntilUtc > nowUtc || lease <= TimeSpan.Zero) return false;
        FeedLeaseUntilUtc = nowUtc.Add(lease);
        return true;
    }

    /// <returns>The items to add; the stored ones are updated, deactivated or brought back in place.</returns>
    public Result<IReadOnlyList<BusinessFormCatalogItem>> ApplyFeed(IReadOnlyList<BusinessFormCatalogItem> stored,
        IReadOnlyList<BusinessFormFeedItem> items, DateTime nowUtc)
    {
        if (Kind != BusinessFormCatalogKind.External) return FormCatalogErrors.ReadOnly;
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.TextEs) && string.IsNullOrWhiteSpace(item.TextEn))
            || items.DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            return FormCatalogErrors.FeedInvalid;
        var byCode = stored.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        if (byCode.Count + items.Count(item => !byCode.ContainsKey(item.Code)) > MaxItems) return FormCatalogErrors.TooManyItems;

        var changed = false;
        var added = new List<BusinessFormCatalogItem>();
        var position = stored.Count == 0 ? 0 : stored.Max(item => item.Position);
        foreach (var item in items)
        {
            if (!byCode.TryGetValue(item.Code, out var current))
            {
                added.Add(BusinessFormCatalogItem.Create(Id, item.Code, item.TextEs, item.TextEn, null, null, ++position, item.Value));
                continue;
            }
            if (current.TextEs != item.TextEs || current.TextEn != item.TextEn || current.Value != item.Value || current.Code != item.Code)
            {
                current.Update(item.Code, item.TextEs, item.TextEn, current.DetailEs, current.DetailEn, current.Position, item.Value);
                changed = true;
            }
            if (!current.IsActive)
            {
                current.SetActive(true);
                changed = true;
            }
        }
        var codes = items.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in stored.Where(item => item.IsActive && !codes.Contains(item.Code)))
        {
            gone.SetActive(false);
            changed = true;
        }
        if (changed || added.Count > 0) Revision++;
        (FeedRefreshedAtUtc, FeedFailedAtUtc, FeedFailureCode, FeedLeaseUntilUtc) = (nowUtc, null, null, null);
        return added;
    }

    public void RecordFeedFailure(string failureCode, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        (FeedFailedAtUtc, FeedFailureCode, FeedLeaseUntilUtc) = (nowUtc, failureCode, null);
    }
}
