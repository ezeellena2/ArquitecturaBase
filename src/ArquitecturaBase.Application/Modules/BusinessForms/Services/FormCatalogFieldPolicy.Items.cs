using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogFieldPolicy
{
    public static Result ValidateItems(BusinessFormCatalog catalog, IReadOnlyList<BusinessFormCatalogItem> items,
        IReadOnlyList<FormCatalogManualItemSaveRequest?> inputs, FormCatalogFieldContext context)
    {
        var sourceIndexes = context.Sources.ToDictionary(pair => pair.Key, pair => CreateSourceIndex(pair.Value));
        var values = items.ToDictionary(item => item.Id, item => ReadValues(item.FieldValuesJson));
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
                        || !source.Contains(selection.GetGuid())))
                    parsed = FormCatalogErrors.FieldValueInvalid;
                if (parsed.IsFailure)
                {
                    if (context.TypeChanges.Contains(field.Id))
                    {
                        if (invalid.Count < 50) invalid.Add(new(item.Id, item.Code));
                        continue;
                    }
                    var path = inputIndexes.TryGetValue(item.Id, out var index) ? $"items[{index}].fields.{key}" : $"fields[{fieldIndex}].type";
                    return FieldErrors.On(parsed.Error, path);
                }
                if (parsed.Value is not null) converted++;
                normalized.Add(item.Id, parsed.Value);
                if (field.Unique && item.IsActive && parsed.Value is { } value && !unique.Add(value.GetRawText()))
                {
                    var path = inputIndexes.TryGetValue(item.Id, out var index) ? $"items[{index}].fields.{key}" : $"fields[{fieldIndex}].unique";
                    return FieldErrors.On(FormCatalogErrors.FieldValueDuplicate, path);
                }
            }
            if (invalid.Count > 0) return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldTypeNotConvertible,
                new Dictionary<string, object?> { ["convertedCount"] = converted, ["invalidRows"] = invalid }), $"fields[{fieldIndex}].type");
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
    }
}
