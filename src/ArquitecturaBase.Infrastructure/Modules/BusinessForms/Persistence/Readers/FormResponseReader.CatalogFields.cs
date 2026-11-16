using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormResponseReader
{
    private sealed class CatalogMetadataRow
    {
        public Guid Id { get; init; }
        public string ValueType { get; init; } = "Text";
        public string? FieldsJson { get; init; }
        public string NameJson { get; init; } = "{}";
    }

    // The catalog's current name by language; the dashboard shows it in the request's language (DOC7-A).
    private static Dictionary<string, string> ReadCatalogName(string nameJson) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(nameJson, JsonSerializerOptions.Web) ?? [];

    private sealed class HistoricalCatalogFieldRow
    {
        public Guid CatalogId { get; init; }
        public Guid Id { get; init; }
        public string Key { get; init; } = "";
        public string NameJson { get; init; } = "{}";
        public string Type { get; init; } = "text";
        public string Role { get; init; } = "data";
        public int Position { get; init; }
    }

    // The union of the fields the responses sealed is kept by the capture; reading it is an index lookup.
    private IQueryable<HistoricalCatalogFieldRow> FindHistoricalCatalogFields(Guid definitionId, Guid[] catalogIds) =>
        dbContext.Set<BusinessFormRecordCatalogField>().AsNoTracking()
            .Where(field => field.DefinitionId == definitionId && catalogIds.Contains(field.CatalogId))
            .Select(field => new HistoricalCatalogFieldRow { CatalogId = field.CatalogId, Id = field.FieldId, Key = field.Key,
                NameJson = field.NameJson, Type = field.Type, Role = field.Role, Position = field.Position });

    private static Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> ReadCatalogFields(CatalogMetadataRow[] catalogs,
        HistoricalCatalogFieldRow[] historical)
    {
        var byCatalog = historical.ToLookup(field => field.CatalogId);
        var result = new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>>();
        foreach (var catalog in catalogs)
        {
            var fields = catalog.FieldsJson is null ? []
                : JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The catalog fields are invalid.");
            var current = fields.Where(field => field.IsActive).OrderBy(field => field.Position).ThenBy(field => field.Id)
                .Select(field => new FormResponseCatalogFieldRow(field.Id, field.Key, field.Name, field.Type, field.Role));
            var saved = byCatalog[catalog.Id].OrderBy(field => field.Position).ThenBy(field => field.Id).Select(field =>
                new FormResponseCatalogFieldRow(field.Id, field.Key,
                    JsonSerializer.Deserialize<Dictionary<string, string>>(field.NameJson, JsonSerializerOptions.Web)
                        ?? throw new InvalidOperationException("The sealed catalog field name is invalid."), field.Type, field.Role));
            result.Add(catalog.Id, current.Concat(saved).DistinctBy(field => field.Id).ToArray());
        }
        return result;
    }
}
