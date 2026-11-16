using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAssistantRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_Removed_RemovalRequestedAtUtc_Id",
                table: "AiConversations",
                columns: new[] { "RemovalRequestedAtUtc", "Id" },
                filter: "\"State\" = 'Removed'");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_Content_ContentExpiresAtUtc_Id",
                table: "AiAssistantTurns",
                columns: new[] { "ContentExpiresAtUtc", "Id" },
                filter: "\"ProtectedQuestion\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_ConversationId_ExpiresAtUtc",
                table: "AiAssistantRequestReceipts",
                columns: new[] { "ConversationId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiConversations_Removed_RemovalRequestedAtUtc_Id",
                table: "AiConversations");

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_Content_ContentExpiresAtUtc_Id",
                table: "AiAssistantTurns");

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantRequestReceipts_ConversationId_ExpiresAtUtc",
                table: "AiAssistantRequestReceipts");
        }
    }
}
