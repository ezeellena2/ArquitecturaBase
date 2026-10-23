using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormPersonDataReader
{
    public async Task<IReadOnlyList<FormPersonAttributeUseRow>> ListAttributeUsagesAsync(IReadOnlyCollection<Guid> attributeIds,
        CancellationToken cancellationToken) => attributeIds.Count == 0 ? [] : await FindAttributeUsages(attributeIds).ToListAsync(cancellationToken);

    private IQueryable<FormPersonAttributeUseRow> FindAttributeUsages(IReadOnlyCollection<Guid> attributeIds) =>
        dbContext.Database.SqlQueryRaw<FormPersonAttributeUseRow>("""
            SELECT requested."AttributeId", definition."Id" AS "DefinitionId", definition."NameJson"::text AS "NameJson"
            FROM "FormDefinitions" AS definition
            CROSS JOIN UNNEST({0}::uuid[]) AS requested("AttributeId")
            CROSS JOIN LATERAL jsonb_array_elements(definition."SchemaJson" -> 'fields') AS question(value)
            WHERE NOT definition."IsDeleted" AND question.value #> ARRAY['source', 'match'] @>
                jsonb_build_array(jsonb_build_object('personAttributeId', requested."AttributeId"::text))
            UNION
            SELECT requested."AttributeId", definition."Id" AS "DefinitionId", definition."NameJson"::text AS "NameJson"
            FROM "FormDefinitions" AS definition
            JOIN "BusinessFormVersions" AS version ON version."Id" = definition."ActiveVersionId" AND version."DefinitionId" = definition."Id"
            CROSS JOIN UNNEST({0}::uuid[]) AS requested("AttributeId")
            CROSS JOIN LATERAL jsonb_array_elements(version."SchemaJson" -> 'questions') AS question(value)
            WHERE NOT definition."IsDeleted" AND question.value #> ARRAY['source', 'match'] @>
                jsonb_build_array(jsonb_build_object('personAttributeId', requested."AttributeId"::text))
            ORDER BY "AttributeId", "DefinitionId"
            """, attributeIds.ToArray());
}
