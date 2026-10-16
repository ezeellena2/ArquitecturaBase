using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RuleSetId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "BusinessFormRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Received");

            migrationBuilder.CreateTable(
                name: "BusinessFormActionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DependsOnRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    OutcomeJson = table.Column<string>(type: "jsonb", nullable: true),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CanRetry = table.Column<bool>(type: "boolean", nullable: false),
                    RetryRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormActionRuns", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormActionRuns_RecordId_Id", x => new { x.RecordId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormActionRuns_BusinessFormActionRuns_RecordId_Depe~",
                        columns: x => new { x.RecordId, x.DependsOnRunId },
                        principalTable: "BusinessFormActionRuns",
                        principalColumns: new[] { "RecordId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormActionRuns_BusinessFormRecords_DefinitionId_Rec~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormActionRuns_BusinessFormRuleSets_DefinitionId_Ru~",
                        columns: x => new { x.DefinitionId, x.RuleSetId },
                        principalTable: "BusinessFormRuleSets",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Culture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DesignId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesignRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormDocuments_BusinessFormActionRuns_RecordId_RunId",
                        columns: x => new { x.RecordId, x.RunId },
                        principalTable: "BusinessFormActionRuns",
                        principalColumns: new[] { "RecordId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormDocuments_BusinessFormRecords_DefinitionId_Reco~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessFormDocuments_StoredFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormTasks_BusinessFormActionRuns_RecordId_RunId",
                        columns: x => new { x.RecordId, x.RunId },
                        principalTable: "BusinessFormActionRuns",
                        principalColumns: new[] { "RecordId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormTasks_BusinessFormRecords_DefinitionId_RecordId",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormTasks_BusinessFormRuleSets_DefinitionId_RuleSet~",
                        columns: x => new { x.DefinitionId, x.RuleSetId },
                        principalTable: "BusinessFormRuleSets",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_RuleSetId",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "RuleSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormActionRuns_DefinitionId_RecordId",
                table: "BusinessFormActionRuns",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormActionRuns_DefinitionId_RuleSetId",
                table: "BusinessFormActionRuns",
                columns: new[] { "DefinitionId", "RuleSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormActionRuns_RecordId_DependsOnRunId",
                table: "BusinessFormActionRuns",
                columns: new[] { "RecordId", "DependsOnRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormActionRuns_RecordId_Event_RuleId_ActionId",
                table: "BusinessFormActionRuns",
                columns: new[] { "RecordId", "Event", "RuleId", "ActionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormActionRuns_State_NextCheckAtUtc_AcceptedAtUtc",
                table: "BusinessFormActionRuns",
                columns: new[] { "State", "NextCheckAtUtc", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormDocuments_DefinitionId_RecordId",
                table: "BusinessFormDocuments",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormDocuments_FileId",
                table: "BusinessFormDocuments",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormDocuments_RecordId_RunId",
                table: "BusinessFormDocuments",
                columns: new[] { "RecordId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormDocuments_RunId_Culture",
                table: "BusinessFormDocuments",
                columns: new[] { "RunId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_DefinitionId_RecordId",
                table: "BusinessFormTasks",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_DefinitionId_RuleSetId",
                table: "BusinessFormTasks",
                columns: new[] { "DefinitionId", "RuleSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_RecordId",
                table: "BusinessFormTasks",
                column: "RecordId",
                unique: true,
                filter: "\"State\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_RecordId_RunId",
                table: "BusinessFormTasks",
                columns: new[] { "RecordId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_RunId",
                table: "BusinessFormTasks",
                column: "RunId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRuleSets_DefinitionId_RuleS~",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "RuleSetId" },
                principalTable: "BusinessFormRuleSets",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRuleSets_DefinitionId_RuleS~",
                table: "BusinessFormRecords");

            migrationBuilder.DropTable(
                name: "BusinessFormDocuments");

            migrationBuilder.DropTable(
                name: "BusinessFormTasks");

            migrationBuilder.DropTable(
                name: "BusinessFormActionRuns");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_DefinitionId_RuleSetId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "RuleSetId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "State",
                table: "BusinessFormRecords");
        }
    }
}
