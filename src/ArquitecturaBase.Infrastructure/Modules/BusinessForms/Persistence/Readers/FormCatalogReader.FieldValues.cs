using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormCatalogReader
{
    private async Task<IReadOnlyList<FormCatalogItemResponse>> PresentItemsAsync(Guid catalogId,
        IReadOnlyList<FormCatalogStoredItemRow> items, CancellationToken cancellationToken)
    {
        var fieldsJson = await dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(value => value.Id == catalogId)
            .Select(value => value.FieldsJson).SingleAsync(cancellationToken);
        var fields = fieldsJson is null ? [] : JsonSerializer.Deserialize<BusinessFormCatalogField[]>(fieldsJson, JsonSerializerOptions.Web)!;
        var sources = fields.Where(field => field.Type == "catalog").ToArray();
        var values = items.ToDictionary(item => item.Id, item => ReadItemFields(item.FieldValuesJson));
        var sourceIds = values.Values.SelectMany(row => sources.Select(field => row.GetValueOrDefault(field.Id.ToString("D"))))
            .Where(value => Guid.TryParseExact(value, "D", out _)).Select(value => Guid.Parse(value!)).Distinct().ToArray();
        var sourceItems = await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => sourceIds.Contains(item.Id))
            .Select(item => new { item.Id, item.CatalogId, item.TextEs, item.TextEn }).ToDictionaryAsync(item => item.Id, cancellationToken);
        return items.Select(item =>
        {
            var fieldTexts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var field in sources)
            {
                var key = field.Id.ToString("D");
                if (!values[item.Id].TryGetValue(key, out var value) || !Guid.TryParseExact(value, "D", out var sourceId)
                    || !sourceItems.TryGetValue(sourceId, out var source) || source.CatalogId != field.SourceCatalogId) continue;
                var texts = new Dictionary<string, string>(StringComparer.Ordinal) { ["es"] = source.TextEs };
                if (!string.IsNullOrWhiteSpace(source.TextEn)) texts["en"] = source.TextEn;
                fieldTexts[key] = texts;
            }
            return new FormCatalogItemResponse(item.Id, item.Code,
                new Dictionary<string, string?> { ["es"] = item.TextEs, ["en"] = item.TextEn },
                new Dictionary<string, string?> { ["es"] = item.DetailEs, ["en"] = item.DetailEn },
                item.Position, item.IsActive, item.Value, values[item.Id], fieldTexts);
        }).ToArray();
    }

    private static Dictionary<string, string?> ReadItemFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText(), StringComparer.Ordinal);
    }
}
