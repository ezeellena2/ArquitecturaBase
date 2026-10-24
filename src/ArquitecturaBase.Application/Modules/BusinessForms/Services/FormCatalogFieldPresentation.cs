using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogFieldPresentation
{
    private static readonly string[] SupportedCultures = ["es", "en"];

    public static FormCatalogStoredItemRow[] ToRows(IReadOnlyList<BusinessFormCatalogItem> items) => items.Select(item =>
        new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn,
            item.Position, item.IsActive, item.Value, item.FieldValuesJson)).ToArray();

    public static async Task<FormCatalogDetailResponse> DescribeAsync(FormCatalogDetailResponse detail, string? fieldsJson,
        string? presentationJson, IReadOnlyList<FormCatalogStoredItemRow> items, IFormCatalogReader reader, bool canReadForms, CancellationToken ct,
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
            var usedBy = FormCatalogFieldUsagePolicy.Describe(usesByField[field.Id], canReadForms).ToList();
            if (presentation?.Title.Contains(field.Id.ToString("D")) == true || presentation?.Description.Contains(field.Id.ToString("D")) == true)
                usedBy.Add(new(null, FormRecordPresentationPolicy.Text(detail.Name, culture), null, null, "Catalog"));
            responses.Add(new(field.Id, field.Key, field.Name, field.Type, field.SourceCatalogId, field.Required, field.Unique,
                field.Min, field.Max, field.MaxLength, field.Role, field.Position, field.IsActive,
                values.Count(value => value.ContainsKey(field.Id.ToString("D"))),
                usedBy));
        }
        var sources = sourceItems ?? await SourcesAsync(fields, reader, ct);
        var choices = sources.Values.SelectMany(options => options).ToDictionary(option => option.Id);
        return detail with
        {
            Fields = responses, Presentation = presentation,
            PresentationCheck = Check(presentation, fields, items, choices, detail.ValueType, Cultures(detail.Name)),
        };
    }

    // The catalog's languages: the original, whose name is required, plus the translation when its name is written.
    public static IReadOnlyList<string> Cultures(IReadOnlyDictionary<string, string> name) =>
        SupportedCultures.Where(culture => !string.IsNullOrWhiteSpace(name.GetValueOrDefault(culture))).ToArray();

    // Counts, in each culture, the active options whose composed title or description exceeds what a channel can show.
    public static FormCatalogPresentationCheckResponse Check(FormCatalogPresentation? presentation, IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyList<FormCatalogStoredItemRow> items, IReadOnlyDictionary<Guid, FormCatalogStoredItemRow> choices, string valueType,
        IReadOnlyList<string> cultures)
    {
        var active = items.Where(item => item.IsActive).OrderBy(item => item.Position).ThenBy(item => item.Id).ToArray();
        return new(cultures.Select(culture =>
        {
            var shown = active.Select(item => (item.Id, Shown: FormCatalogPresentationPolicy.Compose(presentation,
                Values(item, fields, choices, culture, valueType)))).ToArray();
            var titles = shown.Where(option => option.Shown.Title.Length > BusinessFormCatalogItem.MaxTextLength)
                .Select(option => new FormCatalogPresentationExample(option.Id, option.Shown.Title)).ToArray();
            var descriptions = shown.Where(option => option.Shown.Description?.Length > BusinessFormCatalogItem.MaxDetailLength)
                .Select(option => new FormCatalogPresentationExample(option.Id, option.Shown.Title)).ToArray();
            return new FormCatalogPresentationCultureCheckResponse(culture, titles.Length, titles.Take(3).ToArray(),
                descriptions.Length, descriptions.Take(3).ToArray());
        }).ToArray());
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
            ["text"] = item.TextFor(culture),
            ["detail"] = item.DetailFor(culture),
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
                values[key] = option?.TextFor(culture);
            }
            else values[key] = Display(field.Type, input, culture);
        }
        return values;
    }

    // The screen format of a typed value, the same one the front shows (the PDF has its own): grouped numbers, a numeric date and Yes/No.
    private static string? Display(string type, string? value, string culture)
    {
        if (value is null) return null;
        if (type == "boolean") return FormCatalogTexts.Get(value == "true" ? "Yes" : "No", culture);
        var locale = CultureInfo.GetCultureInfo(culture == "en" ? "en-US" : "es-AR");
        if (type == "integer") return long.Parse(value, CultureInfo.InvariantCulture).ToString("N0", locale);
        // The canonical decimal has no trailing zeros, so the optional digits keep it exact.
        if (type == "decimal") return decimal.Parse(value, CultureInfo.InvariantCulture).ToString("#,##0.############################", locale);
        if (type == "date") return DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
        return value;
    }
}
