using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateWhatsAppAccessAndBusiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessEventKind",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unclassified");

            migrationBuilder.AlterColumn<string>(
                name: "Mode",
                table: "WhatsAppContacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Bot");

            // Existing conversations become manual without rewriting identity, read cursors or pending sends.
            // Classify history only from structured operator authorship, never from its body.
            migrationBuilder.Sql("""
                UPDATE "WhatsAppContacts"
                SET "Mode" = 'Manual', "ModeRevision" = "ModeRevision" + 1
                WHERE "Mode" = 'Bot';

                UPDATE "WhatsAppMessages"
                SET "Purpose" = 'Business'
                WHERE "Direction" = 'Outbound' AND "OperatorUserId" IS NOT NULL;
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous modes cannot be reconstructed. Rolling back the schema does not turn chats into bots.
            migrationBuilder.DropColumn(
                name: "AccessEventKind",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "WhatsAppMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Mode",
                table: "WhatsAppContacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Bot",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Manual");
        }
    }
}
