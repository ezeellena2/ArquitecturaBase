using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormCatalogReader
{
    public async Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken) =>
        await FindMaximumOfferSize(catalogId, fixedContainmentJson, personFieldIds).FirstOrDefaultAsync(cancellationToken) ?? new(0, "{}");

    private IQueryable<FormCatalogMaximumOfferRow> FindMaximumOfferSize(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds) => dbContext.Database.SqlQueryRaw<FormCatalogMaximumOfferRow>("""
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
}
