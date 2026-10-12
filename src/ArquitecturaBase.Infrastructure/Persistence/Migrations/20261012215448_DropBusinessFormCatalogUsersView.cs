using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropBusinessFormCatalogUsersView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The view was created by SQL in AddBusinessFormCatalogFilters and is not an EF model object.
            migrationBuilder.Sql("DROP VIEW \"BusinessFormCatalogUsers\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW "BusinessFormCatalogUsers" AS
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
                """);
        }
    }
}
