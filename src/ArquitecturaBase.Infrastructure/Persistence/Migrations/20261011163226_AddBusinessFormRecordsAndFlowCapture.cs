using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormRecordsAndFlowCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages");

            migrationBuilder.AddColumn<string>(
                name: "FlowCaptureErrorCode",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowCaptureState",
                table: "WhatsAppMessages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowRecordId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowRecordNumber",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtectedFlowResponse",
                table: "WhatsAppMessages",
                type: "bytea",
                maxLength: 24576,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureErrorCode",
                table: "WhatsAppFlowSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureState",
                table: "WhatsAppFlowSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompletedNumber",
                table: "WhatsAppFlowSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedRecordId",
                table: "WhatsAppFlowSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LinkRevision",
                table: "WhatsAppFlowSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Prefix",
                table: "WhatsAppFlowSessions",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResponseWaMessageId",
                table: "WhatsAppFlowSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastRecordNumber",
                table: "FormDefinitions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BusinessFormFields_DefinitionId_Id",
                table: "BusinessFormFields",
                columns: new[] { "DefinitionId", "Id" });

            migrationBuilder.CreateTable(
                name: "BusinessFormCaptureReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<long>(type: "bigint", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormCaptureReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecords", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormRecords_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormRecords_BusinessFormVersions_DefinitionId_Versi~",
                        columns: x => new { x.DefinitionId, x.VersionId },
                        principalTable: "BusinessFormVersions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecords_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormRecordValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TextValue = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "numeric", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    ChoiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecordValues", x => x.Id);
                    table.CheckConstraint("CK_BusinessFormRecordValues_ExactlyOneValue", "(CASE WHEN \"TextValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DecimalValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DateValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"ChoiceId\" IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordValues_BusinessFormFields_DefinitionId_Fi~",
                        columns: x => new { x.DefinitionId, x.FieldId },
                        principalTable: "BusinessFormFields",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordValues_BusinessFormRecords_DefinitionId_R~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingFlowReplies",
                table: "WhatsAppMessages",
                columns: new[] { "OccurredAtUtc", "Id" },
                filter: "\"Direction\" = 'Inbound' AND \"Kind\" = 'FlowReply' AND \"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "OccurredAtUtc" },
                filter: "\"Direction\" = 'Inbound' AND \"Kind\" <> 'FlowReply' AND \"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_ResponseWaMessageId",
                table: "WhatsAppFlowSessions",
                column: "ResponseWaMessageId",
                unique: true,
                filter: "\"ResponseWaMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCaptureReceipts_DefinitionId_VersionId",
                table: "BusinessFormCaptureReceipts",
                columns: new[] { "DefinitionId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCaptureReceipts_SourceReceiptId",
                table: "BusinessFormCaptureReceipts",
                column: "SourceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_Number",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_VersionId",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_SourceReceiptId",
                table: "BusinessFormRecords",
                column: "SourceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_DefinitionId_FieldId",
                table: "BusinessFormRecordValues",
                columns: new[] { "DefinitionId", "FieldId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_DefinitionId_RecordId",
                table: "BusinessFormRecordValues",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId",
                table: "BusinessFormRecordValues",
                columns: new[] { "RecordId", "FieldId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages",
                column: "FlowSessionId",
                principalTable: "WhatsAppFlowSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropTable(
                name: "BusinessFormCaptureReceipts");

            migrationBuilder.DropTable(
                name: "BusinessFormRecordValues");

            migrationBuilder.DropTable(
                name: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingFlowReplies",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppFlowSessions_ResponseWaMessageId",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BusinessFormFields_DefinitionId_Id",
                table: "BusinessFormFields");

            migrationBuilder.DropColumn(
                name: "FlowCaptureErrorCode",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "FlowCaptureState",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "FlowRecordId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "FlowRecordNumber",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ProtectedFlowResponse",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "CaptureErrorCode",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "CaptureState",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "CompletedNumber",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "CompletedRecordId",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LinkRevision",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "Prefix",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "ResponseWaMessageId",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LastRecordNumber",
                table: "FormDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingInbound",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "OccurredAtUtc" },
                filter: "\"Direction\" = 'Inbound' AND \"ProcessedAtUtc\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages",
                column: "FlowSessionId",
                principalTable: "WhatsAppFlowSessions",
                principalColumn: "Id");
        }
    }
}
