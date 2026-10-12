using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormCatalogFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiltersJson",
                table: "BusinessFormCatalogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs",
                sql: "(\"Kind\" = 'Manual' AND \"FiltersJson\" IS NULL) OR (\"Kind\" = 'Filters' AND \"FiltersJson\" IS NOT NULL)");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW \"BusinessFormCatalogUsers\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FiltersJson",
                table: "BusinessFormCatalogs");
        }
    }
}
