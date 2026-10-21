using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableWhatsAppAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing automation was unconfigured. Enabling the assistant requires an explicit human choice.
            migrationBuilder.Sql("""
                UPDATE "WhatsAppContacts"
                SET "Mode" = 'Manual', "ModeRevision" = "ModeRevision" + 1
                WHERE "Mode" = 'Bot';
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous application cannot read Assistant. Stop it without inventing a former Bot mode.
            migrationBuilder.Sql("""
                UPDATE "WhatsAppContacts"
                SET "Mode" = 'Manual', "ModeRevision" = "ModeRevision" + 1
                WHERE "Mode" = 'Assistant';
                """.ReplaceLineEndings("\n"));
        }
    }
}
