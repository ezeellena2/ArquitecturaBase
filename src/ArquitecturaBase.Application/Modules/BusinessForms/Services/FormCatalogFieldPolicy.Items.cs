using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
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
            foreach (var item in items)
            {
                values[item.Id].TryGetValue(key, out var raw);
                var input = raw.ValueKind == JsonValueKind.Undefined ? null : Input(raw);
                Result<JsonElement?> parsed;
                if (field.Type == "catalog" && context.TypeChanges.Contains(field.Id) && sourceIndexes.TryGetValue(field.SourceCatalogId!.Value, out var conversionSource))
                {
                    var resolved = ResolveCatalogValue(conversionSource, input);
                    parsed = resolved.IsFailure ? resolved.Error : Normalize(field with { Required = field.Required && item.IsActive }, resolved.Value);
                }
                else parsed = Normalize(field with { Required = field.Required && item.IsActive }, input);
                if (parsed.IsSuccess && parsed.Value is { } selection && field.Type == "catalog"
                    && (!sourceIndexes.TryGetValue(field.SourceCatalogId!.Value, out var source)
                        || !(context.TypeChanges.Contains(field.Id) || Fresh(item.Id, key)
                            ? source.Contains(selection.GetGuid()) : source.ContainsAny(selection.GetGuid()))))
                    parsed = FormCatalogErrors.FieldValueInvalid;
                if (parsed.IsFailure)
                {
                    if (context.TypeChanges.Contains(field.Id))
                    {
                        invalidCount++;
                        if (invalid.Count < 50) invalid.Add(new(item.Id, item.Code, item.Position, Attempted(input), InvalidReason(field, input, parsed.Error)));
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
        foreach (var item in items)
        {
            var result = catalog.SetManualItemFields(item, JsonSerializer.Serialize(values[item.Id], JsonSerializerOptions.Web));
            if (result.IsFailure) return result.Error;
        }
        return Result.Success();

        // A saved value may keep pointing to a source option that was deactivated later, never to one outside the source;
        // only a new or changed one needs an active option.
        bool Fresh(Guid itemId, string key) => requested.TryGetValue(itemId, out var sent) && sent.TryGetValue(key, out var value)
            && (!stored[itemId].TryGetValue(key, out var saved) || !JsonElement.DeepEquals(saved, value));
    }

    private static string? Attempted(string? input) => input?.Trim() is { Length: > 0 } value ? value[..Math.Min(value.Length, 300)] : null;

    // The same reasons the editor uses for a cell: missing, out of the field bounds, too long, or not a value of the type.
    private static string InvalidReason(BusinessFormCatalogField field, string? input, Error error)
    {
        if (error.Code == FormCatalogErrors.FieldValueRequired.Code) return "required";
        if (field.Type is "integer" or "decimal" or "date" && Normalize(field with { Required = false, Min = null, Max = null }, input).IsSuccess) return "range";
        return field.Type == "text" && input?.Trim().Length > (field.MaxLength ?? 300) ? "length" : "invalid";
    }
}
