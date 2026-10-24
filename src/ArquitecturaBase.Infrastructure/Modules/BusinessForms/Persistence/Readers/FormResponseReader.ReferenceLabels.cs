using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    // The label of a captured option in the version's culture, then in the other one (a captured option lacks a translation only in
    // its catalog's original language, as FormRecordReferencePolicy.Text reads it), then the offered text of legacy references.
    private IQueryable<ReferenceLabelRow> ReferenceLabels(FormResponseDefinitionRow definition, string culture)
    {
        var presentations = definition.Versions.Select(version => new { versionId = version.Id, culture = version.DisplayCulture(culture) }).ToArray();
        return dbContext.Database.SqlQueryRaw<ReferenceLabelRow>(
            """
            SELECT reference."Id", reference."RecordId", reference."FieldId", reference."CatalogId", reference."ItemId",
                COALESCE(reference."TranslationsJson" -> (presentation.value->>'culture') ->> 'text',
                    reference."TranslationsJson" -> (CASE presentation.value->>'culture' WHEN 'en' THEN 'es' ELSE 'en' END) ->> 'text',
                    reference."Text") AS "Label"
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
