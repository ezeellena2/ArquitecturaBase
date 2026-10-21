using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAssistantSuggestionsAndAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "AK_WhatsAppFlowSessions_Id_ContactId_OperatorUserId",
                table: "WhatsAppFlowSessions");

            migrationBuilder.AddColumn<long>(
                name: "AssistantModeRevision",
                table: "WhatsAppOutgoingMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickRepliesJson",
                table: "WhatsAppOutgoingMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssistantProcessedAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AudioActorUserId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioContentExpiresAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioContentType",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioContextJson",
                table: "WhatsAppMessages",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioFailureCode",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioFailureCount",
                table: "WhatsAppMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioFirstFailureAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AudioLinkRevision",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioMediaId",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AudioRequestId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioSha256",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AudioSize",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioState",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioStorageKey",
                table: "WhatsAppMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickReplyTitlesJson",
                table: "WhatsAppMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OperatorUserId",
                table: "WhatsAppFlowSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssistantContentExpiresAtUtc",
                table: "WhatsAppFlowSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "WhatsAppFlowSessions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Conversation");

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtectedAssistantContent",
                table: "WhatsAppFlowSessions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSuggestionSetId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureOriginJson",
                table: "BusinessFormRecords",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginRecordId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginSuggestionId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposalDecidedAtUtc",
                table: "BusinessFormCatalogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalDecidedByUserId",
                table: "BusinessFormCatalogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposalEvidenceJson",
                table: "BusinessFormCatalogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalId",
                table: "BusinessFormCatalogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposalState",
                table: "BusinessFormCatalogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AudioSeconds",
                table: "AiAssistantTurns",
                type: "numeric",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayNameEs",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), NULLIF(\"NameJson\" ->> 'en', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayNameEn",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), NULLIF(\"NameJson\" ->> 'es', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayDescriptionEs",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), NULLIF(\"DescriptionJson\" ->> 'en', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayDescriptionEn",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), NULLIF(\"DescriptionJson\" ->> 'es', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), '')",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "BusinessFormMissingTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DiscardedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscardedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormMissingTopics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    OriginalCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SuggestionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormSuggestions", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormSuggestions_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormSuggestions_BusinessFormVersions_DefinitionId_S~",
                        columns: x => new { x.DefinitionId, x.SourceVersionId },
                        principalTable: "BusinessFormVersions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormSuggestions_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppAssistantRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuggestionSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuggestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapturedCulture = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    IsAlways = table.Column<bool>(type: "boolean", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModeRevision = table.Column<long>(type: "bigint", nullable: false),
                    LinkRevision = table.Column<long>(type: "bigint", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutgoingId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionOutgoingId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeclinedDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptanceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAssistantRequests", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppAssistantRequests_ActorContext", "(\"ActorUserId\" IS NOT NULL AND \"ContextJson\" IS NOT NULL) OR (\"IsAlways\" AND \"Kind\" = 'Manual')");
                    table.ForeignKey(
                        name: "FK_WhatsAppAssistantRequests_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppAssistantRequests_WhatsAppMessages_SourceMessageId",
                        column: x => x.SourceMessageId,
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppAssistantRequests_WhatsAppOutgoingMessages_Outgoing~",
                        column: x => x.OutgoingId,
                        principalTable: "WhatsAppOutgoingMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormMissingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormMissingRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormMissingRequests_BusinessFormMissingTopics_Topic~",
                        column: x => x.TopicId,
                        principalTable: "BusinessFormMissingTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_ContactId_RequestId",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "ContactId", "RequestId" },
                unique: true,
                filter: "\"Origin\" = 'Assistant'");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_AudioRequestId",
                table: "WhatsAppMessages",
                column: "AudioRequestId",
                unique: true,
                filter: "\"AudioRequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppFlowSession_OperatorOrigin",
                table: "WhatsAppFlowSessions",
                sql: "(\"Origin\" = 'Assistant' AND \"OperatorUserId\" IS NULL) OR (\"Origin\" = 'Conversation' AND \"OperatorUserId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Id_ActiveSuggestionSetId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveSuggestionSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_OriginRecordId",
                table: "BusinessFormRecords",
                column: "OriginRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogs_ProposalId",
                table: "BusinessFormCatalogs",
                column: "ProposalId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs",
                sql: "(\"ProposalId\" IS NULL AND \"ProposalEvidenceJson\" IS NULL AND \"ProposalState\" IS NULL\n    AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL)\nOR (\"Kind\" = 'Filters' AND \"ProposalId\" IS NOT NULL AND \"ProposalEvidenceJson\" IS NOT NULL AND \"ProposalState\" IS NOT NULL AND (\n    (\"ProposalState\" = 'Pending' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL)\n    OR (\"ProposalState\" = 'Approved' AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL)\n    OR (\"ProposalState\" = 'Discarded' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormMissingRequests_SourceReceiptId",
                table: "BusinessFormMissingRequests",
                column: "SourceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormMissingRequests_TopicId_RequestedAtUtc",
                table: "BusinessFormMissingRequests",
                columns: new[] { "TopicId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormMissingRequests_TopicId_SubjectUserId",
                table: "BusinessFormMissingRequests",
                columns: new[] { "TopicId", "SubjectUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormMissingTopics_NormalizedName",
                table: "BusinessFormMissingTopics",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormSuggestions_DefinitionId_Revision",
                table: "BusinessFormSuggestions",
                columns: new[] { "DefinitionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormSuggestions_DefinitionId_SourceVersionId",
                table: "BusinessFormSuggestions",
                columns: new[] { "DefinitionId", "SourceVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceMessageId_Kind",
                table: "WhatsAppAssistantRequests",
                columns: new[] { "ContactId", "SourceMessageId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_SourceRecordId",
                table: "WhatsAppAssistantRequests",
                columns: new[] { "ContactId", "SourceRecordId" },
                unique: true,
                filter: "\"SourceRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_ContactId_State_CreatedAtUtc_Id",
                table: "WhatsAppAssistantRequests",
                columns: new[] { "ContactId", "State", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_OutgoingId",
                table: "WhatsAppAssistantRequests",
                column: "OutgoingId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_RequestId",
                table: "WhatsAppAssistantRequests",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAssistantRequests_SourceMessageId",
                table: "WhatsAppAssistantRequests",
                column: "SourceMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_OriginRecordId",
                table: "BusinessFormRecords",
                column: "OriginRecordId",
                principalTable: "BusinessFormRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FormDefinitions_BusinessFormSuggestions_Id_ActiveSuggestion~",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveSuggestionSetId" },
                principalTable: "BusinessFormSuggestions",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // New automatic facts cannot be represented by the previous binary. Abort before any destructive change.
            migrationBuilder.Sql("""
                LOCK TABLE "WhatsAppFlowSessions", "WhatsAppMessages", "WhatsAppOutgoingMessages" IN ACCESS EXCLUSIVE MODE;
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WhatsAppFlowSessions" WHERE "OperatorUserId" IS NULL)
                        OR EXISTS (SELECT 1 FROM "WhatsAppMessages" WHERE "Origin" = 'Assistant')
                        OR EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" WHERE "Origin" = 'Assistant' OR "Kind" = 3) THEN
                        RAISE EXCEPTION 'Stage five assistant facts cannot be represented by the previous schema.';
                    END IF;
                END;
                $migration$;

                -- A transcript is not the original body. Keep the audio message as ordinary media when reverting.
                UPDATE "WhatsAppMessages" SET "Kind" = 'Media', "Body" = NULL WHERE "Kind" = 'Audio';
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_OriginRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_FormDefinitions_BusinessFormSuggestions_Id_ActiveSuggestion~",
                table: "FormDefinitions");

            migrationBuilder.DropTable(
                name: "BusinessFormMissingRequests");

            migrationBuilder.DropTable(
                name: "BusinessFormSuggestions");

            migrationBuilder.DropTable(
                name: "WhatsAppAssistantRequests");

            migrationBuilder.DropTable(
                name: "BusinessFormMissingTopics");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppOutgoingMessages_ContactId_RequestId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_AudioRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppFlowSession_OperatorOrigin",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Id_ActiveSuggestionSetId",
                table: "FormDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_OriginRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormCatalogs_ProposalId",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "AssistantModeRevision",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "QuickRepliesJson",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "AssistantProcessedAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioActorUserId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioContentExpiresAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioContentType",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioContextJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioFailureCode",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioFailureCount",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioFirstFailureAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioLinkRevision",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioMediaId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioSha256",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioSize",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioState",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AudioStorageKey",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "QuickReplyTitlesJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AssistantContentExpiresAtUtc",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "ProtectedAssistantContent",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "ActiveSuggestionSetId",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "CaptureOriginJson",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "OriginRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "OriginSuggestionId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "ProposalDecidedAtUtc",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "ProposalDecidedByUserId",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "ProposalEvidenceJson",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "ProposalId",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "ProposalState",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "AudioSeconds",
                table: "AiAssistantTurns");

            migrationBuilder.AlterColumn<Guid>(
                name: "OperatorUserId",
                table: "WhatsAppFlowSessions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayNameEs",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), NULLIF(\"NameJson\" ->> 'en', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayNameEn",
                table: "BusinessFormCatalogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), NULLIF(\"NameJson\" ->> 'es', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayDescriptionEs",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), NULLIF(\"DescriptionJson\" ->> 'en', ''), '')",
                oldStored: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayDescriptionEn",
                table: "BusinessFormCatalogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                computedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComputedColumnSql: "COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), NULLIF(\"DescriptionJson\" ->> 'es', ''), '')",
                oldStored: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_WhatsAppFlowSessions_Id_ContactId_OperatorUserId",
                table: "WhatsAppFlowSessions",
                columns: new[] { "Id", "ContactId", "OperatorUserId" });
        }
    }
}
