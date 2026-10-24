using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormPersonDataReader
{
    private sealed class MissingRecipientsSqlRow
    {
        public int Count { get; init; }
        public string[] People { get; init; } = [];
    }

    private sealed class MissingCountSqlRow
    {
        public string AttributeId { get; init; } = "";
        public int Count { get; init; }
    }

    public async Task<FormPersonMissingRecipientsRow> FindRecipientsWithoutDataAsync(IReadOnlyCollection<Guid> attributeIds,
        CancellationToken cancellationToken)
    {
        if (attributeIds.Count == 0) return new(0, []);
        var row = await FindMissingRecipients(attributeIds).SingleAsync(cancellationToken);
        return new(row.Count, row.People);
    }

    public async Task<FormPersonMissingCounts> CountMissingByAttributeAsync(IReadOnlyCollection<Guid> attributeIds, string? search,
        CancellationToken cancellationToken)
    {
        if (attributeIds.Count == 0) return new(new Dictionary<Guid, int>());
        var rows = await CountMissingByAttribute(attributeIds, search).ToListAsync(cancellationToken);
        return new(rows.ToDictionary(row => Guid.ParseExact(row.AttributeId, "D"), row => row.Count));
    }

    // The same people as the administrative table (not deleted, active or not, same display name search), one row per requested key.
    // A catalog value that is not an option of the attribute's current source (active or not) is missing, as in the table and the send.
    private IQueryable<MissingCountSqlRow> CountMissingByAttribute(IReadOnlyCollection<Guid> attributeIds, string? search) =>
        dbContext.Database.SqlQueryRaw<MissingCountSqlRow>("""
            SELECT requested.key AS "AttributeId", (COUNT(person."Id") FILTER (
                    WHERE data."ValuesJson" -> requested.key IS NULL
                        OR data."ValuesJson" -> requested.key = 'null'::jsonb
                        OR data."ValuesJson" ->> requested.key = ''
                        OR attribute."SourceCatalogId" IS NOT NULL AND NOT EXISTS (
                            SELECT 1 FROM "BusinessFormCatalogItems" AS item
                            WHERE item."CatalogId" = attribute."SourceCatalogId"
                                AND item."Id" = CASE WHEN pg_input_is_valid(data."ValuesJson" ->> requested.key, 'uuid')
                                    THEN (data."ValuesJson" ->> requested.key)::uuid END)))::integer AS "Count"
            FROM UNNEST({0}::text[]) AS requested(key)
            LEFT JOIN "BusinessFormPersonAttributes" AS attribute ON attribute."Id" = requested.key::uuid
            LEFT JOIN "AspNetUsers" AS person ON NOT person."IsDeleted"
                AND ({1} = '' OR person."DisplayName" ILIKE {1} ESCAPE {2})
            LEFT JOIN "BusinessFormPersonData" AS data ON data."UserId" = person."Id"
            GROUP BY requested.key
            """.ReplaceLineEndings("\n"), attributeIds.Select(id => id.ToString("D")).ToArray(),
            string.IsNullOrWhiteSpace(search) ? "" : LikePatterns.Contains(search.Trim()), LikePatterns.EscapeCharacter);

    private IQueryable<MissingRecipientsSqlRow> FindMissingRecipients(IReadOnlyCollection<Guid> attributeIds) =>
        dbContext.Database.SqlQueryRaw<MissingRecipientsSqlRow>("""
            WITH missing AS (
                SELECT person."Id", person."DisplayName"
                FROM "AspNetUsers" AS person
                LEFT JOIN "BusinessFormPersonData" AS data ON data."UserId" = person."Id"
                WHERE person."IsActive" AND NOT person."IsDeleted"
                    AND EXISTS (
                        SELECT 1 FROM "AspNetUserRoles" AS user_role
                        JOIN "AspNetRoleClaims" AS claim ON claim."RoleId" = user_role."RoleId"
                        WHERE user_role."UserId" = person."Id" AND claim."ClaimType" = {1} AND claim."ClaimValue" = {2})
                    AND EXISTS (
                        SELECT 1 FROM UNNEST({0}::text[]) AS required(key)
                        WHERE data."ValuesJson" -> required.key IS NULL
                            OR data."ValuesJson" -> required.key = 'null'::jsonb
                            OR data."ValuesJson" ->> required.key = ''))
            SELECT COUNT(*)::integer AS "Count", ARRAY(
                SELECT "DisplayName" FROM missing
                WHERE NULLIF(BTRIM(COALESCE("DisplayName", '')), '') IS NOT NULL
                ORDER BY "DisplayName", "Id" LIMIT 5)::text[] AS "People"
            FROM missing
            """.ReplaceLineEndings("\n"), attributeIds.Select(id => id.ToString("D")).ToArray(), Permissions.ClaimType, BusinessFormsPermissions.RecordsCreate);
}
