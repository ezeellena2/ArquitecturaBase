using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAssistantRequestReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedDayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantRequestReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovalRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiredPermissions = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                    table.UniqueConstraint("AK_AiConversations_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                });

            migrationBuilder.CreateTable(
                name: "AiAssistantTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ProtectedQuestion = table.Column<string>(type: "text", nullable: true),
                    ProtectedAnswer = table.Column<string>(type: "text", nullable: true),
                    ProtectedEvidences = table.Column<string>(type: "text", nullable: true),
                    PayloadRevision = table.Column<long>(type: "bigint", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ConnectionRevision = table.Column<long>(type: "bigint", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderSteps = table.Column<int>(type: "integer", nullable: false),
                    ToolCalls = table.Column<int>(type: "integer", nullable: false),
                    ToolBytes = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    UsageUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    ReportedUsageSteps = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantTurns", x => x.Id);
                    table.CheckConstraint("CK_AiAssistantTurns_Ordinal", "\"Ordinal\" > 0");
                    table.ForeignKey(
                        name: "FK_AiAssistantTurns_AiConversations_ConversationId_OwnerUserId",
                        columns: x => new { x.ConversationId, x.OwnerUserId },
                        principalTable: "AiConversations",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiAssistantToolExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ToolCallId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToolId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RequiredPermissions = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProtectedResult = table.Column<string>(type: "text", nullable: true),
                    Bytes = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantToolExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAssistantToolExecutions_AiAssistantTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "AiAssistantTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_AcceptedDayUtc_OwnerUserId",
                table: "AiAssistantRequestReceipts",
                columns: new[] { "AcceptedDayUtc", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_ExpiresAtUtc",
                table: "AiAssistantRequestReceipts",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantRequestReceipts_OwnerUserId_RequestId",
                table: "AiAssistantRequestReceipts",
                columns: new[] { "OwnerUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantToolExecutions_TurnId_Ordinal",
                table: "AiAssistantToolExecutions",
                columns: new[] { "TurnId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_ContentExpiresAtUtc",
                table: "AiAssistantTurns",
                column: "ContentExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_ConversationId",
                table: "AiAssistantTurns",
                column: "ConversationId",
                unique: true,
                filter: "\"State\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_ConversationId_Ordinal",
                table: "AiAssistantTurns",
                columns: new[] { "ConversationId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_ConversationId_OwnerUserId",
                table: "AiAssistantTurns",
                columns: new[] { "ConversationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId",
                table: "AiAssistantTurns",
                column: "OwnerUserId",
                unique: true,
                filter: "\"State\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_State_DeadlineAtUtc",
                table: "AiAssistantTurns",
                columns: new[] { "State", "DeadlineAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_State_SubmittedAtUtc_Id",
                table: "AiAssistantTurns",
                columns: new[] { "State", "SubmittedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_LastActivityAtUtc_Id",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "LastActivityAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAssistantRequestReceipts");

            migrationBuilder.DropTable(
                name: "AiAssistantToolExecutions");

            migrationBuilder.DropTable(
                name: "AiAssistantTurns");

            migrationBuilder.DropTable(
                name: "AiConversations");
        }
    }
}
