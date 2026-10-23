using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogFieldsAndPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldValuesJson",
                table: "BusinessFormRecordReferences",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldsJson",
                table: "BusinessFormCatalogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationJson",
                table: "BusinessFormCatalogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldValuesJson",
                table: "BusinessFormCatalogItems",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_ManualFields",
                table: "BusinessFormCatalogs",
                sql: "\"Kind\" = 'Manual' OR (\"FieldsJson\" IS NULL AND \"PresentationJson\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogItems_FieldValuesJson",
                table: "BusinessFormCatalogItems",
                column: "FieldValuesJson")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormCatalogs", "BusinessFormCatalogItems", "BusinessFormRecordReferences" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormCatalogs" WHERE "FieldsJson" IS NOT NULL OR "PresentationJson" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "BusinessFormCatalogItems" WHERE "FieldValuesJson" <> '{}'::jsonb)
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordReferences" WHERE "FieldValuesJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Catalog fields and captured values cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_ManualFields",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormCatalogItems_FieldValuesJson",
                table: "BusinessFormCatalogItems");

            migrationBuilder.DropColumn(
                name: "FieldValuesJson",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "FieldsJson",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "PresentationJson",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FieldValuesJson",
                table: "BusinessFormCatalogItems");
        }
    }
}
