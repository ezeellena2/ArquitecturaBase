using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormCatalogReader
{
    public async Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken, Guid? groupByFieldId = null)
    {
        if (groupByFieldId is { } groupBy)
        {
            var grouped = await FindGroupedMaximumOfferSize(catalogId, fixedContainmentJson, personFieldIds, groupBy).FirstOrDefaultAsync(cancellationToken);
            return grouped is null ? new(0, "{}", 0, 0) : new(grouped.Maximum, grouped.ValuesJson, grouped.Groups, grouped.LargestGroup);
        }
        var row = await FindMaximumOfferSize(catalogId, fixedContainmentJson, personFieldIds).FirstOrDefaultAsync(cancellationToken);
        return row is null ? new(0, "{}") : new(row.Maximum, row.ValuesJson);
    }

    private IQueryable<MaximumSqlRow> FindMaximumOfferSize(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds) => dbContext.Database.SqlQueryRaw<MaximumSqlRow>("""
            WITH grouped AS (
                SELECT COUNT(*)::integer AS "Maximum", COALESCE((
                    SELECT jsonb_object_agg(requested.key, item."FieldValuesJson" -> requested.key ORDER BY requested.key)
                    FROM UNNEST({2}::text[]) AS requested(key)), jsonb_build_object()) AS "Values"
                FROM "BusinessFormCatalogItems" AS item
                WHERE item."CatalogId" = {0} AND item."IsActive"
                    AND item."FieldValuesJson" @> {1}::jsonb
                    AND NOT EXISTS (
                        SELECT 1 FROM UNNEST({2}::text[]) AS required(key)
                        WHERE item."FieldValuesJson" -> required.key IS NULL
                            OR item."FieldValuesJson" -> required.key = 'null'::jsonb)
                GROUP BY "Values")
            SELECT "Maximum", "Values"::text AS "ValuesJson"
            FROM grouped ORDER BY "Maximum" DESC, "Values"::text LIMIT 1
            """.ReplaceLineEndings("\n"), catalogId, fixedContainmentJson ?? "{}", personFieldIds.Select(id => id.ToString("D")).ToArray());

    // LD1-A: the same groups by person, each counted by the value of the field chosen first, as the values list it (`->>`, an
    // empty text counts with the options without a value). The row is the person group with the most options; the values and
    // the largest value are the most any person gets, because WhatsApp's 200 apply to each of them.
    private IQueryable<GroupedMaximumSqlRow> FindGroupedMaximumOfferSize(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, Guid groupByFieldId) => dbContext.Database.SqlQueryRaw<GroupedMaximumSqlRow>("""
            WITH per_value AS (
                SELECT COALESCE((
                    SELECT jsonb_object_agg(requested.key, item."FieldValuesJson" -> requested.key ORDER BY requested.key)
                    FROM UNNEST({2}::text[]) AS requested(key)), jsonb_build_object()) AS "Values",
                    NULLIF(item."FieldValuesJson" ->> {3}, '') AS "Group", COUNT(*)::integer AS "Count"
                FROM "BusinessFormCatalogItems" AS item
                WHERE item."CatalogId" = {0} AND item."IsActive"
                    AND item."FieldValuesJson" @> {1}::jsonb
                    AND NOT EXISTS (
                        SELECT 1 FROM UNNEST({2}::text[]) AS required(key)
                        WHERE item."FieldValuesJson" -> required.key IS NULL
                            OR item."FieldValuesJson" -> required.key = 'null'::jsonb)
                GROUP BY "Values", "Group"),
            per_person AS (
                SELECT "Values", SUM("Count")::integer AS "Maximum", COUNT(*)::integer AS "Groups", MAX("Count") AS "LargestGroup"
                FROM per_value GROUP BY "Values")
            SELECT "Maximum", "Values"::text AS "ValuesJson",
                (SELECT MAX("Groups") FROM per_person) AS "Groups", (SELECT MAX("LargestGroup") FROM per_person) AS "LargestGroup"
            FROM per_person ORDER BY "Maximum" DESC, "Values"::text LIMIT 1
            """.ReplaceLineEndings("\n"), catalogId, fixedContainmentJson ?? "{}", personFieldIds.Select(id => id.ToString("D")).ToArray(),
            groupByFieldId.ToString("D"));

    private sealed class MaximumSqlRow
    {
        public int Maximum { get; init; }
        public string ValuesJson { get; init; } = "{}";
    }

    private sealed class GroupedMaximumSqlRow
    {
        public int Maximum { get; init; }
        public string ValuesJson { get; init; } = "{}";
        public int Groups { get; init; }
        public int LargestGroup { get; init; }
    }
}
