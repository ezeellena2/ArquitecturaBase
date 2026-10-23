using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormCatalogReader
{
    public async Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds,
        CancellationToken cancellationToken) => await FieldUsages(catalogId, fieldIds,
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es").ToListAsync(cancellationToken);

    private IQueryable<FormCatalogFieldUsageRow> FieldUsages(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, string culture) =>
        dbContext.Database.SqlQueryRaw<FormCatalogFieldUsageRow>("""
            SELECT definition."Id" AS "DefinitionId",
                COALESCE(NULLIF(definition."NameJson" ->> {0}, ''), definition."NameJson" ->> definition."Culture", '') AS "FormName",
                COALESCE(NULLIF(question.value #>> ARRAY['label', {0}], ''), question.value #>> ARRAY['label', definition."Culture"], '') AS "Question",
                'Draft' AS "Stage", requested."FieldId" AS "FieldId"
            FROM "FormDefinitions" AS definition
            CROSS JOIN UNNEST({2}::uuid[]) AS requested("FieldId")
            CROSS JOIN LATERAL jsonb_array_elements(definition."SchemaJson" -> 'fields') AS question(value)
            WHERE NOT definition."IsDeleted" AND question.value #>> ARRAY['source', 'catalog'] = {1} AND (
                (question.value -> 'source') @> jsonb_build_object('match', jsonb_build_array(jsonb_build_object('fieldId', requested."FieldId"::text)))
                OR question.value #> ARRAY['source', 'presentation', 'title'] @> jsonb_build_array(requested."FieldId"::text)
                OR question.value #> ARRAY['source', 'presentation', 'description'] @> jsonb_build_array(requested."FieldId"::text))
            UNION ALL
            SELECT definition."Id" AS "DefinitionId",
                COALESCE(NULLIF(version."NameJson" ->> {0}, ''), version."NameJson" ->> version."Culture", definition."NameJson" ->> definition."Culture", '') AS "FormName",
                COALESCE(NULLIF(question.value #>> ARRAY['label', {0}], ''), question.value #>> ARRAY['label', COALESCE(version."Culture", definition."Culture")], '') AS "Question",
                'Active' AS "Stage", requested."FieldId" AS "FieldId"
            FROM "FormDefinitions" AS definition
            JOIN "BusinessFormVersions" AS version ON version."Id" = definition."ActiveVersionId" AND version."DefinitionId" = definition."Id"
            CROSS JOIN UNNEST({2}::uuid[]) AS requested("FieldId")
            CROSS JOIN LATERAL jsonb_array_elements(version."SchemaJson" -> 'questions') AS question(value)
            WHERE NOT definition."IsDeleted" AND question.value #>> ARRAY['source', 'catalog'] = {1} AND (
                (question.value -> 'source') @> jsonb_build_object('match', jsonb_build_array(jsonb_build_object('fieldId', requested."FieldId"::text)))
                OR question.value #> ARRAY['source', 'presentation', 'title'] @> jsonb_build_array(requested."FieldId"::text)
                OR question.value #> ARRAY['source', 'presentation', 'description'] @> jsonb_build_array(requested."FieldId"::text))
            ORDER BY "FormName", "DefinitionId", "Question", "Stage"
            """, culture, catalogId.ToString("D"), fieldIds.ToArray());

    public async Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken cancellationToken)
    {
        var containment = JsonSerializer.Serialize(new[] { new { sourceCatalogId = catalogId, isActive = true } });
        return await dbContext.Set<BusinessFormCatalog>().AsNoTracking().AnyAsync(value => value.Id != catalogId
            && value.Kind == BusinessFormCatalogKind.Manual && EF.Functions.JsonContains(value.FieldsJson!, containment), cancellationToken)
            || await dbContext.Set<BusinessFormPersonAttribute>().AsNoTracking()
                .AnyAsync(value => value.IsActive && value.SourceCatalogId == catalogId, cancellationToken);
    }
}
