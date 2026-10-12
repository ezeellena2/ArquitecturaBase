using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class BusinessFormsCatalogUsersView
{
    internal static Task EnsureCreatedAsync(ApiFactory factory, CancellationToken cancellationToken) =>
        factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE VIEW "BusinessFormCatalogUsers" AS
            SELECT users."Id",
                   COALESCE(users."DisplayName", '') AS "Name",
                   ARRAY(SELECT roles."Name"
                         FROM "AspNetUserRoles" AS user_roles
                         JOIN "AspNetRoles" AS roles ON roles."Id" = user_roles."RoleId"
                         WHERE user_roles."UserId" = users."Id"
                         ORDER BY roles."Name")::text[] AS "Roles",
                   users."IsActive" AND NOT users."IsDeleted" AS "Active"
            FROM "AspNetUsers" AS users
            WHERE NOT users."IsDeleted";
            """, cancellationToken));
}
