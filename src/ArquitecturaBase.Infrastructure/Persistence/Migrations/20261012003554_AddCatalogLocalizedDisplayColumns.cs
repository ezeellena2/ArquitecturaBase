using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogLocalizedDisplayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayDescriptionEn",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), '')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayDescriptionEs",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), '')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameEn",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), '')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameEs",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), '')",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayDescriptionEn",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "DisplayDescriptionEs",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "DisplayNameEn",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "DisplayNameEs",
                table: "BusinessFormCatalogs");
        }
    }
}
