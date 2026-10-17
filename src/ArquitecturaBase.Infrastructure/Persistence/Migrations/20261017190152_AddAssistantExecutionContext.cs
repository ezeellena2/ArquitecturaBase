using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantExecutionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiConversations_OwnerUserId_LastActivityAtUtc_Id",
                table: "AiConversations");

            migrationBuilder.AddColumn<string>(
                name: "ChannelId",
                table: "AiConversations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "home");

            migrationBuilder.AddColumn<string>(
                name: "ExecutionProfileJson",
                table: "AiAssistantTurns",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_ChannelId_LastActivityAtUtc_Id",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "ChannelId", "LastActivityAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiConversations_OwnerUserId_ChannelId_LastActivityAtUtc_Id",
                table: "AiConversations");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "AiConversations");

            migrationBuilder.DropColumn(
                name: "ExecutionProfileJson",
                table: "AiAssistantTurns");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_LastActivityAtUtc_Id",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "LastActivityAtUtc", "Id" });
        }
    }
}
