using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppSpreadsheetLoads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Footer",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetRowsJson",
                table: "WhatsAppOutgoingMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetRowsJson",
                table: "WhatsAppMessages",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous image neither sends the footer of a load nor shows «Por qué lo propuso» of the notice of the loads: dropping them
            // would lose what was sent and why. Preserve them: refuse before any DDL, never delete them.
            migrationBuilder.Sql("""
                LOCK TABLE "WhatsAppMessages", "WhatsAppOutgoingMessages" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" WHERE "Footer" IS NOT NULL OR "SpreadsheetRowsJson" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "WhatsAppMessages" WHERE "SpreadsheetRowsJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'The loads of spreadsheets and the notices that explain them must be removed before restoring the previous schema.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropColumn(
                name: "Footer",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetRowsJson",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetRowsJson",
                table: "WhatsAppMessages");
        }
    }
}
