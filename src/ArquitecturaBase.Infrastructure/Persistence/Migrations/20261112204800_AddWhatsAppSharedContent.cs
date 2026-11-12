using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppSharedContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsResend",
                table: "WhatsAppOutgoingMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "WhatsAppMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhonesJson",
                table: "WhatsAppMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsResend",
                table: "WhatsAppMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocationAddress",
                table: "WhatsAppMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtectedSharedValues",
                table: "WhatsAppFlowSessions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedContentJson",
                table: "WhatsAppFlowSessions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous image sends one Flow per send: a resent form would read there as a second Flow of its send, and what a send
            // took of what the person shared has no place. Preserve them: refuse before any DDL, never delete them.
            migrationBuilder.Sql("""
                LOCK TABLE "WhatsAppMessages", "WhatsAppOutgoingMessages", "WhatsAppFlowSessions" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WhatsAppMessages" WHERE "IsResend")
                        OR EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" WHERE "IsResend")
                        OR EXISTS (SELECT 1 FROM "WhatsAppFlowSessions" WHERE "SharedContentJson" IS NOT NULL OR "ProtectedSharedValues" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Resent forms and what their sends took of the shared content must be removed before restoring the previous schema.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            // The previous image does not know a contact (EF cannot read an enum name it does not have): it goes back to the message of
            // another kind it was before W3, without a body and keeping its row. Its name and its phones go with their columns, like a
            // received document's descriptor, and so does the address of a place, whose point stays.
            migrationBuilder.Sql("UPDATE \"WhatsAppMessages\" SET \"Kind\" = 'Other' WHERE \"Kind\" = 'Contact';");

            migrationBuilder.DropColumn(
                name: "IsResend",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ContactPhonesJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "IsResend",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "LocationAddress",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ProtectedSharedValues",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "SharedContentJson",
                table: "WhatsAppFlowSessions");
        }
    }
}
