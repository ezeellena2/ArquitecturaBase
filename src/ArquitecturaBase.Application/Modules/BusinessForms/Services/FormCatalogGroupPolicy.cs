using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// LD1-A (2026-11-07): the values of the field a catalog question chooses first by. A text field's values go in alphabetical
/// order and a catalog field's, in the order and texts of its source; the options without a value come last, together.
/// </summary>
internal static class FormCatalogGroupPolicy
{
    public static IReadOnlyList<FormEntryGroupResponse> Present(BusinessFormCatalogField field, IReadOnlyList<FormEntryGroupRow> rows,
        IReadOnlyList<FormCatalogStoredItemRow> sourceItems, string culture)
    {
        var values = rows.Where(row => !string.IsNullOrEmpty(row.Value)).Select(row => row.Value!).Distinct(StringComparer.Ordinal).ToArray();
        var groups = new List<FormEntryGroupResponse>();
        if (field.Type == "catalog")
        {
            var source = sourceItems.ToDictionary(item => item.Id);
            var known = values.Select(value => (Value: value, Item: Guid.TryParseExact(value, "D", out var id) ? source.GetValueOrDefault(id) : null)).ToArray();
            groups.AddRange(known.Where(value => value.Item is not null).OrderBy(value => value.Item!.Position).ThenBy(value => value.Item!.Id)
                .Select(value => new FormEntryGroupResponse(value.Value, value.Item!.TextFor(culture))));
            // A value that is no longer an option of the source keeps its stored text, after the known ones.
            groups.AddRange(known.Where(value => value.Item is null).Select(value => value.Value).Order(Comparer(culture))
                .Select(value => new FormEntryGroupResponse(value, value)));
        }
        else groups.AddRange(values.Order(Comparer(culture)).Select(value => new FormEntryGroupResponse(value, value)));
        if (rows.Any(row => string.IsNullOrEmpty(row.Value)))
            groups.Add(new(null, string.Format(CultureInfo.GetCultureInfo(culture), FormCatalogTexts.Get("GroupWithoutValue", culture),
                Lowercase(FormRecordPresentationPolicy.Text(field.Name, culture), culture))));
        return groups;
    }

    /// <summary>
    /// The containment of the question's filters plus the value chosen first; null when a filter already fixes that field to
    /// another value, so no option can match.
    /// </summary>
    public static string? Containment(string? containment, Guid fieldId, string value)
    {
        var values = containment is null ? new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            : new SortedDictionary<string, JsonElement>(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(containment, JsonSerializerOptions.Web)!,
                StringComparer.Ordinal);
        var key = fieldId.ToString("D");
        var chosen = JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
        if (values.TryGetValue(key, out var fixedValue)) return fixedValue.GetRawText() == chosen.GetRawText() ? containment : null;
        values.Add(key, chosen);
        return JsonSerializer.Serialize(values, JsonSerializerOptions.Web);
    }

    private static StringComparer Comparer(string culture) =>
        StringComparer.Create(CultureInfo.GetCultureInfo(culture == "en" ? "en-US" : "es-AR"), CompareOptions.IgnoreCase);

    // «Sin rubro»: the name's first letter goes in lowercase unless it starts an acronym, like «IVA».
    private static string Lowercase(string name, string culture) =>
        name.Length > 1 && char.IsUpper(name[0]) && !char.IsUpper(name[1])
            ? char.ToLower(name[0], CultureInfo.GetCultureInfo(culture)) + name[1..] : name;
}
