using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// F24: a repeated creation request is the same request only when the catalog still is what it asked for: its revision has not moved
// and its texts, options and fields match, matched by option code and field key (the order a user can change). No new columns.
internal static class FormCatalogCreationReplayPolicy
{
    public static bool Matches(BusinessFormCatalog catalog, IReadOnlyList<BusinessFormCatalogItem> items, CreateManualCatalogRequest request)
    {
        if (catalog.Revision != 1 || catalog.Kind != BusinessFormCatalogKind.Manual) return false;
        if (request.ValueType is not null && request.ValueType != catalog.ValueType.ToString()) return false;
        if (request.ValueType is null && catalog.ValueType != BusinessFormCatalogValueType.Text) return false;
        if (!SameStoredTexts(request.Name, ReadTexts(catalog.NameJson)) || !SameStoredTexts(request.Description, ReadTexts(catalog.DescriptionJson))) return false;
        if (request.Presentation is null != (catalog.PresentationJson is null)) return false;
        var sentItems = request.Items ?? [];
        if (sentItems.Count != items.Count) return false;
        var byCode = items.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var sent in sentItems)
        {
            if (sent?.Code is null || !byCode.TryGetValue(sent.Code.Trim(), out var item)) return false;
            var value = FormCatalogValuePolicy.Normalize(catalog.ValueType, sent.Value);
            if (value.IsFailure || value.Value != item.Value || sent.IsActive != item.IsActive) return false;
            if (!SameTexts(sent.Text, new Dictionary<string, string?> { ["es"] = item.TextEs, ["en"] = item.TextEn })
                || !SameTexts(sent.Detail, new Dictionary<string, string?> { ["es"] = item.DetailEs, ["en"] = item.DetailEn })) return false;
        }
        var sentFields = request.Fields ?? [];
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        if (sentFields.Count != fields.Count) return false;
        var byKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        return sentFields.All(sent => sent?.Key is not null && byKey.TryGetValue(sent.Key, out var field)
            && sent.Type == field.Type && sent.SourceCatalogId == field.SourceCatalogId && sent.Required == field.Required
            && sent.Unique == field.Unique && sent.MaxLength == field.MaxLength && sent.Role == field.Role && sent.IsActive == field.IsActive
            && SameStoredTexts(sent.Name, field.Name));
    }

    private static bool SameTexts(IReadOnlyDictionary<string, string?>? sent, IReadOnlyDictionary<string, string?> stored)
    {
        var left = FormCatalogProposalPolicy.Texts(sent);
        var right = FormCatalogProposalPolicy.Texts(stored);
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var text) && text == pair.Value);
    }

    private static bool SameStoredTexts(IReadOnlyDictionary<string, string?>? sent, IReadOnlyDictionary<string, string> stored) =>
        SameTexts(sent, stored.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? [];
}
