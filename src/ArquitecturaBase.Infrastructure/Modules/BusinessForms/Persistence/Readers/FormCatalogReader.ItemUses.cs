using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormCatalogReader
{
    // What keeps each requested option from being deleted, in the request order and then records, options, people, forms. An
    // item id is unique, so finding it as a JSON value is finding a reference to it: in another option's fields, in a person's
    // data, or anywhere in the draft, the active version, the active rules or the active suggestions of a form not deleted.
    public async Task<IReadOnlyList<FormCatalogItemUsageRow>> ListItemUsesAsync(Guid catalogId, IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken) => await dbContext.Database.SqlQueryRaw<FormCatalogItemUsageRow>("""
            SELECT requested."ItemId" AS "ItemId", usage."Kind" AS "Kind"
            FROM UNNEST({1}::uuid[]) WITH ORDINALITY AS requested("ItemId", "Order")
            CROSS JOIN LATERAL jsonb_build_object('id', requested."ItemId"::text) AS item(vars)
            CROSS JOIN LATERAL (
                SELECT 'records' AS "Kind", 1 AS "Rank" WHERE EXISTS (
                    SELECT 1 FROM "BusinessFormRecordReferences" AS reference
                    WHERE reference."CatalogId" = {0} AND reference."ItemId" = requested."ItemId")
                UNION ALL
                SELECT 'options', 2 WHERE EXISTS (
                    SELECT 1 FROM "BusinessFormCatalogItems" AS other
                    WHERE jsonb_path_exists(other."FieldValuesJson", '$.* ? (@ == $id)', item.vars))
                UNION ALL
                SELECT 'people', 3 WHERE EXISTS (
                    SELECT 1 FROM "BusinessFormPersonData" AS person
                    WHERE jsonb_path_exists(person."ValuesJson", '$.* ? (@ == $id)', item.vars))
                UNION ALL
                SELECT 'forms', 4 WHERE EXISTS (
                    SELECT 1 FROM "FormDefinitions" AS definition
                    LEFT JOIN "BusinessFormVersions" AS version ON version."Id" = definition."ActiveVersionId" AND version."DefinitionId" = definition."Id"
                    LEFT JOIN "BusinessFormRuleSets" AS rules ON rules."Id" = definition."ActiveRuleSetId"
                    LEFT JOIN "BusinessFormSuggestions" AS suggestions ON suggestions."Id" = definition."ActiveSuggestionSetId"
                    WHERE NOT definition."IsDeleted" AND (
                        jsonb_path_exists(definition."SchemaJson", '$.** ? (@ == $id)', item.vars)
                        OR jsonb_path_exists(version."SchemaJson", '$.** ? (@ == $id)', item.vars)
                        OR jsonb_path_exists(rules."RulesJson", '$.** ? (@ == $id)', item.vars)
                        OR jsonb_path_exists(rules."QuestionSchemaJson", '$.** ? (@ == $id)', item.vars)
                        OR jsonb_path_exists(suggestions."SuggestionsJson", '$.** ? (@ == $id)', item.vars)))
            ) AS usage
            ORDER BY requested."Order", usage."Rank"
            """, catalogId, itemIds.ToArray()).ToListAsync(cancellationToken);
}
