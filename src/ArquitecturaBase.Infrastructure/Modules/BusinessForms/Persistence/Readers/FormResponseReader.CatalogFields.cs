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
    }
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

    private IQueryable<HistoricalCatalogFieldRow> FindHistoricalCatalogFields(Guid definitionId) =>
        dbContext.Database.SqlQueryRaw<HistoricalCatalogFieldRow>("""
            SELECT DISTINCT ON (reference."CatalogId", entry.value ->> 'id')
                reference."CatalogId", (entry.value ->> 'id')::uuid AS "Id", entry.value ->> 'key' AS "Key",
                (entry.value -> 'name')::text AS "NameJson", entry.value ->> 'type' AS "Type", entry.value ->> 'role' AS "Role",
                entry.position::integer AS "Position"
            FROM "BusinessFormRecordReferences" AS reference
            JOIN "BusinessFormRecords" AS record ON record."Id" = reference."RecordId" AND record."DefinitionId" = reference."DefinitionId"
            CROSS JOIN LATERAL jsonb_array_elements(reference."FieldValuesJson") WITH ORDINALITY AS entry(value, position)
            WHERE reference."DefinitionId" = {0} AND reference."FieldValuesJson" IS NOT NULL
            ORDER BY reference."CatalogId", entry.value ->> 'id', record."Number" DESC, reference."Id" DESC
            """.ReplaceLineEndings("\n"), definitionId);

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
