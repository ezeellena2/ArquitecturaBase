using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    private IQueryable<ReferenceLabelRow> ReferenceLabels(FormResponseDefinitionRow definition, string culture)
    {
        var presentations = definition.Versions.Select(version => new { versionId = version.Id, culture = version.DisplayCulture(culture) }).ToArray();
        return dbContext.Database.SqlQueryRaw<ReferenceLabelRow>(
            """
            SELECT reference."Id", reference."RecordId", reference."FieldId", reference."CatalogId", reference."ItemId",
                COALESCE(reference."TranslationsJson" -> (presentation.value->>'culture') ->> 'text',
                    reference."TranslationsJson" -> 'es' ->> 'text', reference."Text") AS "Label"
            FROM "BusinessFormRecordReferences" reference
            JOIN "BusinessFormRecords" record ON record."Id" = reference."RecordId"
            JOIN jsonb_array_elements({0}::jsonb) presentation(value) ON (presentation.value->>'versionId')::uuid = record."VersionId"
            """, JsonSerializer.Serialize(presentations, JsonSerializerOptions.Web));
    }
    private sealed class ReferenceLabelRow
    {
        public Guid Id { get; init; }
        public Guid RecordId { get; init; }
        public Guid FieldId { get; init; }
        public Guid CatalogId { get; init; }
        public Guid ItemId { get; init; }
        public string Label { get; init; }="";
    }
}
