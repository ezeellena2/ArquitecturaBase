using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAssistantActiveTurnsByConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelConversationId",
                table: "AiAssistantTurns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId", "ChannelConversationId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running')\r\nAND (CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) IS DISTINCT FROM '1'::jsonb")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The actor/channel index cannot represent concurrent interpretation scopes. Preserve accepted work.
            migrationBuilder.Sql("""
                LOCK TABLE "AiAssistantTurns" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT "OwnerUserId", "ChannelId" FROM "AiAssistantTurns"
                        WHERE "ChannelId" <> 'home' AND "State" IN ('Pending', 'Running')
                          AND (CASE WHEN "ExecutionProfileJson" IS JSON OBJECT THEN "ExecutionProfileJson"::jsonb -> 'purpose' END)
                              IS DISTINCT FROM '1'::jsonb
                        GROUP BY "OwnerUserId", "ChannelId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Active assistant conversation scopes must finish before restoring actor-channel uniqueness.';
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "ChannelConversationId",
                table: "AiAssistantTurns");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running')\nAND (CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) IS DISTINCT FROM '1'::jsonb");
        }
    }
}
