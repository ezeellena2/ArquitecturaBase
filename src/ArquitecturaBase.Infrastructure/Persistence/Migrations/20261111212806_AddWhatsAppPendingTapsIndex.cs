using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppPendingTapsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingTaps",
                table: "WhatsAppMessages",
                column: "OccurredAtUtc",
                filter: "\"Direction\" = 'Inbound' AND \"Kind\" = 'ButtonReply' AND \"AssistantProcessedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingTaps",
                table: "WhatsAppMessages");
        }
    }
}
