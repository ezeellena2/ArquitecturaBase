using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppAssistantAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceMessageId_Kind",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.AddColumn<string>(
                name: "AssistantReplyJson",
                table: "WhatsAppOutgoingMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAssistantHandoff",
                table: "WhatsAppOutgoingMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AssistantReplyJson",
                table: "WhatsAppMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandoffAtUtc",
                table: "WhatsAppContacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoffReason",
                table: "WhatsAppContacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoffReasonCode",
                table: "WhatsAppContacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActionOrdinal",
                table: "WhatsAppAssistantRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationRequestId",
                table: "WhatsAppAssistantRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeJson",
                table: "WhatsAppAssistantRequests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppAssistantAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Profile = table.Column<string>(type: "jsonb", nullable: false),
                    Cases = table.Column<string>(type: "jsonb", nullable: false),
                    Fallback = table.Column<string>(type: "jsonb", nullable: false),
                    Limits = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAssistantAgents", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppAssistantAgents_OriginalCulture", "\"OriginalCulture\" IN ('es', 'en')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_HandoffAtUtc",
                table: "WhatsAppContacts",
                column: "HandoffAtUtc",
                filter: "\"HandoffAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceMessageId_Kind_Ac~",
                table: "WhatsAppAssistantRequests",
                columns: new[] { "ContactId", "SourceMessageId", "Kind", "ActionOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantAgents_IsMain",
                table: "WhatsAppAssistantAgents",
                column: "IsMain",
                unique: true,
                filter: "\"IsMain\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would lose the agent, the conversations that need a person and the why of the replies, and the previous
            // unique index would reject the other actions of a reply. Preserve them: refuse before any DDL, never delete them.
            migrationBuilder.Sql("""
                LOCK TABLE "WhatsAppContacts", "WhatsAppMessages", "WhatsAppOutgoingMessages", "WhatsAppAssistantRequests", "WhatsAppAssistantAgents"
                    IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WhatsAppAssistantAgents")
                        OR EXISTS (SELECT 1 FROM "WhatsAppContacts" WHERE "HandoffAtUtc" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "WhatsAppMessages" WHERE "AssistantReplyJson" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" WHERE "AssistantReplyJson" IS NOT NULL OR "IsAssistantHandoff")
                        OR EXISTS (SELECT 1 FROM "WhatsAppAssistantRequests"
                            WHERE "ActionOrdinal" <> 0 OR "OperationRequestId" IS NOT NULL OR "OutcomeJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'The assistant agent, its handoffs and the why of its replies must be removed before restoring the previous schema.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropTable(
                name: "WhatsAppAssistantAgents");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppContacts_HandoffAtUtc",
                table: "WhatsAppContacts");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceMessageId_Kind_Ac~",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.DropColumn(
                name: "AssistantReplyJson",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "IsAssistantHandoff",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "AssistantReplyJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "HandoffAtUtc",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "HandoffReason",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "HandoffReasonCode",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "ActionOrdinal",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.DropColumn(
                name: "OperationRequestId",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.DropColumn(
                name: "OutcomeJson",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceMessageId_Kind",
                table: "WhatsAppAssistantRequests",
                columns: new[] { "ContactId", "SourceMessageId", "Kind" },
                unique: true);
        }
    }
}
