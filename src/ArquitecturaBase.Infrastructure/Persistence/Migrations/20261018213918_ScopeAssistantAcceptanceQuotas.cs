using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAssistantAcceptanceQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId", table: "AiAssistantRequestReceipts");
            migrationBuilder.AddColumn<string>(name: "ChannelId", table: "AiAssistantRequestReceipts", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsAudio", table: "AiAssistantRequestReceipts", type: "boolean", nullable: true);
            // Preserve only attribution proven by retained content; orphan receipts remain unknown.
            migrationBuilder.Sql("""
                UPDATE "AiAssistantRequestReceipts" AS receipt
                SET "ChannelId" = turn."ChannelId"
                FROM "AiAssistantTurns" AS turn
                WHERE receipt."TurnId" = turn."Id" AND receipt."OwnerUserId" = turn."OwnerUserId"
                    AND receipt."ConversationId" = turn."ConversationId";
                UPDATE "AiAssistantRequestReceipts" AS receipt
                SET "ChannelId" = conversation."ChannelId"
                FROM "AiConversations" AS conversation
                WHERE receipt."ChannelId" IS NULL AND receipt."ConversationId" = conversation."Id"
                    AND receipt."OwnerUserId" = conversation."OwnerUserId";
                UPDATE "AiAssistantRequestReceipts" SET "IsAudio" = false WHERE "ChannelId" = 'home';
                WITH profiles AS (
                    SELECT "Id", "OwnerUserId", "ChannelId",
                        CASE WHEN "ExecutionProfileJson" IS JSON OBJECT THEN "ExecutionProfileJson"::jsonb END AS profile
                    FROM "AiAssistantTurns"
                )
                UPDATE "AiAssistantRequestReceipts" AS receipt
                SET "IsAudio" = (turn.profile ->> 'purpose' = '1') IS TRUE
                FROM profiles AS turn
                WHERE receipt."TurnId" = turn."Id" AND receipt."OwnerUserId" = turn."OwnerUserId"
                    AND receipt."ChannelId" = turn."ChannelId" AND receipt."IsAudio" IS NULL
                    AND turn.profile ->> 'instructionsVersion' = '1'
                    AND turn.profile ->> 'modelProfileVersion' ~ '^[1-9][0-9]*$'
                    AND length(trim(turn.profile ->> 'modelProfileId')) > 0
                    AND turn.profile #>> '{channelContext,channelId}' = turn."ChannelId"
                    AND turn.profile #>> '{channelContext,actorUserId}' = receipt."OwnerUserId"::text
                    AND (
                        ((NOT (turn.profile ? 'purpose') OR turn.profile ->> 'purpose' = '0')
                            AND turn.profile ->> 'instructionsId' = 'capabilities-v1')
                        OR (turn.profile ->> 'purpose' = '1' AND turn.profile ->> 'instructionsId' = 'audio-none-v1'
                            AND turn.profile #> '{channelContext,allowedCapabilities}' = '[]'::jsonb
                            AND turn.profile #> '{channelContext,allowedTerminals}' = '[]'::jsonb)
                    );
                """);
            migrationBuilder.CreateIndex(name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId_Chann~", table: "AiAssistantRequestReceipts",
                columns: new[] { "AcceptedDayUtc", "OwnerUserId", "ChannelId", "IsAudio" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId_Chann~", table: "AiAssistantRequestReceipts");
            migrationBuilder.DropColumn(name: "ChannelId", table: "AiAssistantRequestReceipts");
            migrationBuilder.DropColumn(name: "IsAudio", table: "AiAssistantRequestReceipts");
            migrationBuilder.CreateIndex(name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId", table: "AiAssistantRequestReceipts",
                columns: new[] { "AcceptedDayUtc", "OwnerUserId" });
        }
    }
}
