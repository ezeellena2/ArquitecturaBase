using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogFieldPresentation
{
    public static FormCatalogStoredItemRow[] ToRows(IReadOnlyList<BusinessFormCatalogItem> items) => items.Select(item =>
        new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn,
            item.Position, item.IsActive, item.Value, item.FieldValuesJson)).ToArray();

    public static async Task<FormCatalogDetailResponse> DescribeAsync(FormCatalogDetailResponse detail, string? fieldsJson,
        string? presentationJson, IReadOnlyList<FormCatalogStoredItemRow> items, IFormCatalogReader reader, CancellationToken ct,
        IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>? sourceItems = null)
    {
        var fields = FormCatalogFieldPolicy.ReadFields(fieldsJson);
        var presentation = presentationJson is null ? null : JsonSerializer.Deserialize<FormCatalogPresentation>(presentationJson, JsonSerializerOptions.Web);
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var values = items.Select(item => FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson)).ToArray();
        var responses = new List<FormCatalogFieldResponse>();
        var fieldIds = fields.Where(field => field.IsActive).Select(field => field.Id).ToArray();
        var uses = fieldIds.Length == 0 ? [] : await reader.ListFieldUsageAsync(detail.Id, fieldIds, ct);
        var usesByField = uses.ToLookup(use => use.FieldId);
        foreach (var field in fields)
        {
            var usedBy = usesByField[field.Id].Select(use => new FormCatalogFieldUsageResponse(use.DefinitionId, use.FormName, use.Question, use.Stage)).ToList();
            if (presentation?.Title.Contains(field.Id.ToString("D")) == true || presentation?.Description.Contains(field.Id.ToString("D")) == true)
                usedBy.Add(new(null, detail.Name.GetValueOrDefault(culture) ?? detail.Name["es"], null, "Catalog"));
            responses.Add(new(field.Id, field.Key, field.Name, field.Type, field.SourceCatalogId, field.Required, field.Unique,
                field.Min, field.Max, field.MaxLength, field.Role, field.Position, field.IsActive,
                values.Any(value => value.ContainsKey(field.Id.ToString("D"))),
                usedBy));
        }
        var sources = sourceItems ?? await SourcesAsync(fields, reader, ct);
        var choices = sources.Values.SelectMany(options => options).ToDictionary(option => option.Id);
        var longTitles = items.Where(item => item.IsActive).Select(item =>
        {
            var shown = FormCatalogPresentationPolicy.Compose(presentation, Values(item, fields, choices, culture, detail.ValueType));
            return new FormCatalogPresentationExample(item.Id, shown.Title);
        }).Where(item => item.Title.Length > 30).ToArray();
        return detail with { Fields = responses, Presentation = presentation, PresentationCheck = new(longTitles.Length, longTitles.Take(3).ToArray()) };
    }

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>> SourcesAsync(
        IReadOnlyList<BusinessFormCatalogField> fields, IFormCatalogReader reader, CancellationToken ct)
    {
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var sourceId in fields.Where(field => field.IsActive && field.SourceCatalogId is not null).Select(field => field.SourceCatalogId!.Value).Distinct())
            sources.Add(sourceId, await reader.ListAllItemsAsync(sourceId, ct));
        return sources;
    }

    public static Dictionary<string, string?> Values(FormCatalogStoredItemRow item, IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyDictionary<Guid, FormCatalogStoredItemRow> choices, string culture, string valueType)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["text"] = culture == "en" && !string.IsNullOrWhiteSpace(item.TextEn) ? item.TextEn : item.TextEs,
            ["detail"] = culture == "en" && !string.IsNullOrWhiteSpace(item.DetailEn) ? item.DetailEn : item.DetailEs,
            ["code"] = item.Code,
            ["value"] = Display(valueType.ToLowerInvariant(), item.Value, culture),
        };
        var stored = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        foreach (var field in fields.Where(field => field.IsActive))
        {
            var key = field.Id.ToString("D");
            if (!stored.TryGetValue(key, out var raw)) continue;
            var input = FormCatalogFieldPolicy.Input(raw);
            if (field.Type == "catalog")
            {
                var option = Guid.TryParse(input, out var id) ? choices.GetValueOrDefault(id) : null;
                values[key] = option is null ? null : culture == "en" && !string.IsNullOrWhiteSpace(option.TextEn) ? option.TextEn : option.TextEs;
            }
            else values[key] = Display(field.Type, input, culture);
        }
        return values;
    }

    private static string? Display(string type, string? value, string culture)
    {
        if (value is null) return null;
        if (type == "boolean") return FormCatalogTexts.Get(value == "true" ? "Yes" : "No", culture);
        if (type == "decimal") return culture == "es" ? value.Replace('.', ',') : value;
        if (type == "date") return DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
        return value;
    }
}
