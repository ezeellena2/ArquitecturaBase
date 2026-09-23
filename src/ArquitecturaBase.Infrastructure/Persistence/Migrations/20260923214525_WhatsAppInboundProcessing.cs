using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppInboundProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "OccurredAtUtc" },
                filter: "\"Direction\" = 'Inbound' AND \"ProcessedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages");
        }
    }
}
