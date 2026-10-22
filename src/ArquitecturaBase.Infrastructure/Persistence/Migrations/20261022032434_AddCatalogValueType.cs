using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogValueType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "BusinessFormRecordReferences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "BusinessFormRecordReferences",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "BusinessFormCatalogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Text");

            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "BusinessFormCatalogItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecordReferences_Value",
                table: "BusinessFormRecordReferences",
                sql: "(\"Value\" IS NULL) = (\"ValueType\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs",
                sql: "\"ValueType\" IN ('Text','Integer','Decimal','Date') AND (\"Kind\" = 'Manual' OR \"ValueType\" = 'Text')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A previous image cannot represent typed values. Refuse the rollback before any schema change.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormCatalogs", "BusinessFormCatalogItems", "BusinessFormRecordReferences" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormCatalogs" WHERE "ValueType" <> 'Text')
                        OR EXISTS (SELECT 1 FROM "BusinessFormCatalogItems" WHERE "Value" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordReferences" WHERE "Value" IS NOT NULL OR "ValueType" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Typed catalog values cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecordReferences_Value",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "ValueType",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "ValueType",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "BusinessFormCatalogItems");
        }
    }
}
