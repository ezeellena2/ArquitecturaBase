using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantDocumentReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns");

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId_Chann~",
                table: "AiAssistantRequestReceipts");

            migrationBuilder.AddColumn<bool>(
                name: "IsDocument",
                table: "AiAssistantRequestReceipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The receipts before documents are not readings. A reading accepted before a Down keeps its sealed purpose (2) in its turn:
            // its receipt gets back its own quota from that retained content, never from a guess.
            migrationBuilder.Sql("""
                UPDATE "AiAssistantRequestReceipts" AS receipt
                SET "IsDocument" = TRUE
                FROM "AiAssistantTurns" AS turn
                WHERE receipt."TurnId" = turn."Id" AND receipt."OwnerUserId" = turn."OwnerUserId" AND receipt."IsAudio" IS NOT TRUE
                    AND (CASE WHEN turn."ExecutionProfileJson" IS JSON OBJECT THEN turn."ExecutionProfileJson"::jsonb -> 'purpose' END) = '2'::jsonb;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId", "ChannelConversationId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running') AND ((CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) IS NULL OR (CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) NOT IN ('1'::jsonb, '2'::jsonb))")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_IsDocument_OwnerU~",
                table: "AiAssistantRequestReceipts",
                columns: new[] { "AcceptedDayUtc", "IsDocument", "OwnerUserId", "ChannelId", "IsAudio" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiAssistantRequestReceipts_AudioOrDocument",
                table: "AiAssistantRequestReceipts",
                sql: "NOT (\"IsAudio\" IS TRUE AND \"IsDocument\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous index would take an active reading for an interpretation of its channel conversation. Preserve accepted
            // work: refuse to go back while a reading is active, never closing or deleting it.
            migrationBuilder.Sql("""
                LOCK TABLE "AiAssistantTurns" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "AiAssistantTurns"
                        WHERE "ChannelId" <> 'home' AND "State" IN ('Pending', 'Running')
                          AND (CASE WHEN "ExecutionProfileJson" IS JSON OBJECT THEN "ExecutionProfileJson"::jsonb -> 'purpose' END) = '2'::jsonb
                    ) THEN
                        RAISE EXCEPTION 'Active assistant document readings must finish before restoring the previous active-turn uniqueness.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns");

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_IsDocument_OwnerU~",
                table: "AiAssistantRequestReceipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiAssistantRequestReceipts_AudioOrDocument",
                table: "AiAssistantRequestReceipts");

            migrationBuilder.DropColumn(
                name: "IsDocument",
                table: "AiAssistantRequestReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId", "ChannelConversationId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running') AND (CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) IS DISTINCT FROM '1'::jsonb")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId_Chann~",
                table: "AiAssistantRequestReceipts",
                columns: new[] { "AcceptedDayUtc", "OwnerUserId", "ChannelId", "IsAudio" });
        }
    }
}
