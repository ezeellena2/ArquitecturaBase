using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogFieldPolicy
{
    public static Result ValidateItems(BusinessFormCatalog catalog, IReadOnlyList<BusinessFormCatalogItem> items,
        IReadOnlyList<FormCatalogManualItemSaveRequest?> inputs, IReadOnlyDictionary<Guid, Dictionary<string, JsonElement>> requested,
        FormCatalogFieldContext context)
    {
        var sourceIndexes = context.Sources.ToDictionary(pair => pair.Key, pair => CreateSourceIndex(pair.Value));
        var stored = items.ToDictionary(item => item.Id, item => ReadValues(item.FieldValuesJson));
        var values = items.ToDictionary(item => item.Id, item => new Dictionary<string, JsonElement>(
            requested.TryGetValue(item.Id, out var sent) ? sent : stored[item.Id], StringComparer.Ordinal));
        var inputIndexes = inputs.Select((input, index) => (input, index)).Where(pair => pair.input?.ItemId is not null)
            .ToDictionary(pair => pair.input!.ItemId!.Value, pair => pair.index);
        // Items arrive in storage order; walking them by row keeps invalidRows as the first rows and the first error stable.
        var byRow = items.OrderBy(item => item.Position).ThenBy(item => item.Id).ToArray();
        for (var fieldIndex = 0; fieldIndex < context.Fields.Count; fieldIndex++)
        {
            var field = context.Fields[fieldIndex];
            if (!field.IsActive) continue;
            var key = field.Id.ToString("D");
            if (context.RequiredTransitions.Contains(field.Id))
            {
                var missing = items.Count(item => item.IsActive && (!values[item.Id].TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(Input(raw))));
                if (missing > 0) return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldValuesMissing,
                    new Dictionary<string, object?> { ["missingCount"] = missing }), $"fields[{fieldIndex}].required");
            }
            var normalized = new Dictionary<Guid, JsonElement?>();
            var invalid = new List<FormCatalogInvalidFieldRow>();
            var converted = 0;
            var invalidCount = 0;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var converting = context.TypeChanges.Contains(field.Id);
            foreach (var item in byRow)
            {
                values[item.Id].TryGetValue(key, out var raw);
                var input = raw.ValueKind == JsonValueKind.Undefined ? null : Input(raw);
                var fresh = Fresh(item.Id, key);
                var sent = Sent(item.Id, key);
                var rowField = field with { Required = field.Required && item.IsActive };
                // A saved option or boolean becomes the text it showed, which is also what a row that does not fit reports.
                var shown = converting && !sent ? Shown(context.Previous[field.Id], field, input, catalog.OriginalCulture, sourceIndexes) : input;
                if (shown.IsSuccess) input = shown.Value;
                var parsed = shown.IsFailure ? shown.Error
                    : converting ? Convert(context.Previous[field.Id], rowField, input, sent, context, sourceIndexes)
                    : Normalize(rowField, input);
                if (parsed.IsSuccess && parsed.Value is { } selection && field.Type == "catalog"
                    && (!sourceIndexes.TryGetValue(field.SourceCatalogId!.Value, out var source)
                        || !(converting || fresh ? source.Contains(selection.GetGuid()) : source.ContainsAny(selection.GetGuid()))))
                    parsed = FormCatalogErrors.FieldValueInvalid;
                if (parsed.IsFailure)
                {
                    if (converting)
                    {
                        invalidCount++;
                        if (invalid.Count < 50) invalid.Add(new(item.Id, item.Code, item.Position, Attempted(input), shown.IsFailure ? "invalid"
                            : InvalidReason(field, input, parsed.Error, unbounded => Convert(context.Previous[field.Id], unbounded, input, sent, context, sourceIndexes))));
                        continue;
                    }
                    var path = inputIndexes.TryGetValue(item.Id, out var index) ? $"items[{index}].fields.{key}" : $"fields[{fieldIndex}].type";
                    return FieldErrors.On(parsed.Error, path);
                }
                if (parsed.Value is not null) converted++;
                normalized.Add(item.Id, parsed.Value);
                if (field.Unique && item.IsActive && parsed.Value is { } value && !unique.Add(Input(value)!))
                {
                    var path = inputIndexes.TryGetValue(item.Id, out var index) ? $"items[{index}].fields.{key}" : $"fields[{fieldIndex}].unique";
                    return FieldErrors.On(FormCatalogErrors.FieldValueDuplicate, path);
                }
            }
            if (invalid.Count > 0) return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldTypeNotConvertible,
                new Dictionary<string, object?> { ["convertedCount"] = converted, ["invalidCount"] = invalidCount, ["invalidRows"] = invalid }), $"fields[{fieldIndex}].type");
            foreach (var (itemId, value) in normalized)
            {
                if (value is { } present) values[itemId][key] = present;
                else values[itemId].Remove(key);
            }
        }
        // An item whose values did not change keeps its stored JSON, so a save neither rewrites it nor touches its audit.
        foreach (var item in items.Where(item => !SameValues(stored[item.Id], values[item.Id])))
        {
            var result = catalog.SetManualItemFields(item, JsonSerializer.Serialize(values[item.Id], JsonSerializerOptions.Web));
            if (result.IsFailure) return result.Error;
        }
        return Result.Success();

        // A saved value may keep pointing to a source option that was deactivated later, never to one outside the source;
        // only a new or changed one needs an active option.
        bool Fresh(Guid itemId, string key) => requested.TryGetValue(itemId, out var sent) && sent.TryGetValue(key, out var value)
            && (!stored[itemId].TryGetValue(key, out var saved) || !JsonElement.DeepEquals(saved, value));

        // A value received in this save was edited by the user, even when it equals the saved text.
        bool Sent(Guid itemId, string key) => inputIndexes.TryGetValue(itemId, out var index) && inputs[index]!.Fields?.ContainsKey(key) == true;
    }

    // A catalog target resolves the source option by identity, code or text. Otherwise a value received in this save already
    // arrives canonical in the new type, even when it equals the saved text, while a saved one is read like an imported cell:
    // text in the request culture, where an ambiguous number does not convert, and a value of another type in its canonical form.
    private static Result<JsonElement?> Convert(BusinessFormCatalogField previous, BusinessFormCatalogField field, string? input, bool sent,
        FormCatalogFieldContext context, Dictionary<Guid, FormCatalogFieldSourceIndex> sourceIndexes)
    {
        if (field.Type == "catalog" && sourceIndexes.TryGetValue(field.SourceCatalogId!.Value, out var source))
        {
            var resolved = ResolveCatalogValue(source, input);
            return resolved.IsFailure ? resolved.Error : Normalize(field, resolved.Value);
        }
        return sent ? Normalize(field, input) : NormalizeImport(field, input, context.Culture, canonical: previous.Type != "text");
    }

    // Text keeps what a saved value showed: a source option by its text and a boolean as Yes or No, in the original language of
    // the catalog that owns the field, because the text becomes its content. A value that is no option of its source does not convert.
    private static Result<string?> Shown(BusinessFormCatalogField previous, BusinessFormCatalogField field, string? input, string culture,
        Dictionary<Guid, FormCatalogFieldSourceIndex> sourceIndexes)
    {
        if (field.Type != "text" || string.IsNullOrWhiteSpace(input)) return input;
        return previous.Type switch
        {
            "boolean" => FormCatalogTexts.Get(input == "true" ? "Yes" : "No", culture),
            "catalog" => Guid.TryParseExact(input, "D", out var id) && sourceIndexes[previous.SourceCatalogId!.Value].Text(id, culture) is { } text
                ? text : FormCatalogErrors.FieldValueInvalid,
            _ => input,
        };
    }

    // jsonb comes back with its own spacing, key order and escapes, so the stored map is compared by value, not by text.
    private static bool SameValues(Dictionary<string, JsonElement> stored, Dictionary<string, JsonElement> values) =>
        stored.Count == values.Count && values.All(pair => stored.TryGetValue(pair.Key, out var saved) && JsonElement.DeepEquals(saved, pair.Value));

    private static string? Attempted(string? input) => input?.Trim() is { Length: > 0 } value ? value[..Math.Min(value.Length, 300)] : null;

    // The same reasons the editor uses for a cell: missing, out of the field bounds, too long, or not a value of the type.
    // The bounds are judged with the conversion's own reading, so an ambiguous number is not a value of the type.
    private static string InvalidReason(BusinessFormCatalogField field, string? input, Error error, Func<BusinessFormCatalogField, Result<JsonElement?>> read)
    {
        if (error.Code == FormCatalogErrors.FieldValueRequired.Code) return "required";
        if (field.Type is "integer" or "decimal" or "date" && read(field with { Required = false, Min = null, Max = null }).IsSuccess) return "range";
        return field.Type == "text" && input?.Trim().Length > (field.MaxLength ?? 300) ? "length" : "invalid";
    }
}
