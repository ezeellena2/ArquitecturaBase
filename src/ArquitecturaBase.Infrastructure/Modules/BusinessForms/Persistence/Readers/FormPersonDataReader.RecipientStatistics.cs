using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormPersonDataReader
{
    private sealed class MissingRecipientsSqlRow
    {
        public int Count { get; init; }
        public string[] People { get; init; } = [];
    }

    public async Task<FormPersonMissingRecipientsRow> FindRecipientsWithoutDataAsync(IReadOnlyCollection<Guid> attributeIds,
        CancellationToken cancellationToken)
    {
        if (attributeIds.Count == 0) return new(0, []);
        var row = await FindMissingRecipients(attributeIds).SingleAsync(cancellationToken);
        return new(row.Count, row.People);
    }

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
