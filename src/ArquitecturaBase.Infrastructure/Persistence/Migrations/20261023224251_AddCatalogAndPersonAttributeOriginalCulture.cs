using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndPersonAttributeOriginalCulture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalCulture",
                table: "BusinessFormPersonAttributes",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "es");

            migrationBuilder.AddColumn<string>(
                name: "OriginalCulture",
                table: "BusinessFormCatalogs",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "es");

            // Assistant proposals already sealed their original culture in the evidence; ordinary catalogs and person attributes were Spanish.
            migrationBuilder.Sql("""
                UPDATE "BusinessFormCatalogs" SET "OriginalCulture" = "ProposalEvidenceJson" ->> 'originalCulture'
                WHERE "ProposalEvidenceJson" IS NOT NULL;
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous image assumes a Spanish original. Only a proposal keeps an English original in its evidence.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormCatalogs", "BusinessFormPersonAttributes" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormCatalogs" WHERE "OriginalCulture" <> 'es' AND "ProposalEvidenceJson" IS NULL)
                        OR EXISTS (SELECT 1 FROM "BusinessFormPersonAttributes" WHERE "OriginalCulture" <> 'es')
                    THEN
                        RAISE EXCEPTION 'A non-Spanish original catalog or person attribute cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropColumn(
                name: "OriginalCulture",
                table: "BusinessFormPersonAttributes");

            migrationBuilder.DropColumn(
                name: "OriginalCulture",
                table: "BusinessFormCatalogs");
        }
    }
}
