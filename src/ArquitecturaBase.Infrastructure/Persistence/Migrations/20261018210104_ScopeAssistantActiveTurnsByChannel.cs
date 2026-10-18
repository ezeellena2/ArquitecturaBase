using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAssistantActiveTurnsByChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId",
                table: "AiAssistantTurns");

            migrationBuilder.AddColumn<string>(
                name: "ChannelId",
                table: "AiAssistantTurns",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "home");

            // Existing turns keep the channel captured by their owning conversation.
            migrationBuilder.Sql("""
                UPDATE "AiAssistantTurns" AS turn
                SET "ChannelId" = conversation."ChannelId"
                FROM "AiConversations" AS conversation
                WHERE turn."ConversationId" = conversation."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous actor-wide index cannot represent simultaneous channel or Home work.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT "OwnerUserId" FROM "AiAssistantTurns"
                        WHERE "State" IN ('Pending', 'Running')
                        GROUP BY "OwnerUserId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Active assistant operations must finish before restoring actor-wide uniqueness.';
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "AiAssistantTurns");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId",
                table: "AiAssistantTurns",
                column: "OwnerUserId",
                unique: true,
                filter: "\"State\" IN ('Pending', 'Running')");
        }
    }
}
