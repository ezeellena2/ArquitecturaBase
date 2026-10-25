using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogFieldCapturePolicy
{
    public static async Task<IReadOnlyDictionary<Guid, string>> CaptureAsync(IFormRecordRepository records,
        IReadOnlyCollection<Guid> selectedItemIds, CancellationToken cancellationToken)
    {
        if (selectedItemIds.Count == 0) return new Dictionary<Guid, string>();
        var itemIds = selectedItemIds.Distinct().Order().ToArray();
        var rows = (await records.ListCatalogFieldValuesAsync(itemIds, cancellationToken)).ToDictionary(row => row.ItemId);
        var selected = itemIds.Select(itemId => (ItemId: itemId, Fields: ReadFields(rows[itemId]))).ToArray();
        var sourceIds = selected.SelectMany(item => item.Fields).Where(value => value.Field.Type == "catalog")
            .Select(value => SourceId(value.Value)).OfType<Guid>().Distinct().Order().ToArray();
        var sourceRows = sourceIds.Length == 0 ? new Dictionary<Guid, FormCatalogItemFieldValuesRow>()
            : (await records.ListCatalogFieldValuesAsync(sourceIds, cancellationToken)).ToDictionary(row => row.ItemId);
        var result = new Dictionary<Guid, string>();
        foreach (var item in selected)
        {
            var fields = item.Fields.Select(value => value.Field.Type != "catalog" ? Captured(value.Field, value.Value, null)
                    : SourceText(sourceRows, value.Value, value.Field.SourceCatalogId) is { } text ? Captured(value.Field, value.Value, text) : null)
                .OfType<FormCatalogCapturedField>().ToArray();
            if (fields.Length == 0) continue;
            result[item.ItemId] = FormDraftPolicy.Serialize(fields);
        }
        return result;
    }

    private static FormCatalogCapturedField Captured(BusinessFormCatalogField field, JsonElement value, IReadOnlyDictionary<string, string>? text) =>
        new(field.Id, field.Key, field.Name, field.Type, field.Role, value, text);

    private static Guid? SourceId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;

    private static (BusinessFormCatalogField Field, JsonElement Value)[] ReadFields(FormCatalogItemFieldValuesRow row)
    {
        if (row.FieldsJson is null) return [];
        var fields = FormDraftPolicy.Read<BusinessFormCatalogField[]>(row.FieldsJson);
        using var values = JsonDocument.Parse(row.FieldValuesJson);
        return fields.Where(field => field.IsActive).Select(field =>
            (Field: field, Value: values.RootElement.TryGetProperty(field.Id.ToString("D"), out var value) ? value : default))
            .Where(value => value.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
                && (value.Value.ValueKind != JsonValueKind.String || value.Value.GetString()!.Length > 0))
            .Select(value => (value.Field, value.Value.Clone())).ToArray();
    }

    // Seals the texts the source option has: its catalog's original and, when present, the translation. A saved value
    // that is not an option of the field's current source (one kept from before a source change) has no visible text,
    // so the field is left out of the seal like a field without a value instead of failing the capture.
    private static Dictionary<string, string>? SourceText(Dictionary<Guid, FormCatalogItemFieldValuesRow> rows,
        JsonElement value, Guid? sourceCatalogId)
    {
        if (SourceId(value) is not { } id || !rows.TryGetValue(id, out var row) || row.CatalogId != sourceCatalogId) return null;
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(row.TextEs)) texts["es"] = row.TextEs;
        if (!string.IsNullOrWhiteSpace(row.TextEn)) texts["en"] = row.TextEn;
        return texts.Count == 0 ? null : texts;
    }
}
