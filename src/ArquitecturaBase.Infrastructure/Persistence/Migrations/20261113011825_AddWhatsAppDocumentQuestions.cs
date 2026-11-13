using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppDocumentQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListButton",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A list sent to a person (task 9 of the documents) keeps its button here: the previous schema cannot keep it, so the Down
            // refuses before any DDL instead of losing it.
            migrationBuilder.Sql("""
                LOCK TABLE "WhatsAppOutgoingMessages" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" WHERE "ListButton" IS NOT NULL) THEN
                        RAISE EXCEPTION 'WhatsApp lists with their button cannot be represented by the previous schema.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropColumn(
                name: "ListButton",
                table: "WhatsAppOutgoingMessages");
        }
    }
}
