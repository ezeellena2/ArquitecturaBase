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
            .Select(value => Guid.Parse(value.Value.GetString()!)).Distinct().Order().ToArray();
        var sourceRows = sourceIds.Length == 0 ? new Dictionary<Guid, FormCatalogItemFieldValuesRow>()
            : (await records.ListCatalogFieldValuesAsync(sourceIds, cancellationToken)).ToDictionary(row => row.ItemId);
        var result = new Dictionary<Guid, string>();
        foreach (var item in selected)
        {
            if (item.Fields.Length == 0) continue;
            var fields = item.Fields.Select(value => new FormCatalogCapturedField(value.Field.Id, value.Field.Key,
                value.Field.Name, value.Field.Type, value.Field.Role, value.Value,
                value.Field.Type == "catalog" ? SourceText(sourceRows[Guid.Parse(value.Value.GetString()!)], value.Field.SourceCatalogId!.Value) : null)).ToArray();
            result[item.ItemId] = FormDraftPolicy.Serialize(fields);
        }
        return result;
    }

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

    private static Dictionary<string, string> SourceText(FormCatalogItemFieldValuesRow row, Guid sourceCatalogId)
    {
        if (row.CatalogId != sourceCatalogId || string.IsNullOrWhiteSpace(row.TextEs))
            throw new InvalidOperationException("The captured source catalog option is invalid.");
        var texts = new Dictionary<string, string>(StringComparer.Ordinal) { ["es"] = row.TextEs };
        if (!string.IsNullOrWhiteSpace(row.TextEn)) texts["en"] = row.TextEn;
        return texts;
    }
}
