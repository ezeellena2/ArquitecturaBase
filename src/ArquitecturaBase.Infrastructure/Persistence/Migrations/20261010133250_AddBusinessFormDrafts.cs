using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NameJson = table.Column<string>(type: "jsonb", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    PrefixLocked = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Culture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SubmitLabelJson = table.Column<string>(type: "jsonb", nullable: false),
                    SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    RetiredIdentityLedgerJson = table.Column<string>(type: "jsonb", nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayNameEn = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), \"Name\")", stored: true),
                    DisplayNameEs = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, computedColumnSql: "COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), \"Name\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormDraftChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: true),
                    ConfirmedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultFormat = table.Column<int>(type: "integer", nullable: false),
                    ProtectedResult = table.Column<byte[]>(type: "bytea", nullable: true),
                    ResultExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDraftChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormDraftChanges_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Prefix",
                table: "FormDefinitions",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDraftChanges_DefinitionId",
                table: "FormDraftChanges",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_FormDraftChanges_OperatorUserId_RequestId",
                table: "FormDraftChanges",
                columns: new[] { "OperatorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDraftChanges_ResultExpiresAtUtc_Id",
                table: "FormDraftChanges",
                columns: new[] { "ResultExpiresAtUtc", "Id" },
                filter: "\"ProtectedResult\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormDraftChanges");

            migrationBuilder.DropTable(
                name: "FormDefinitions");
        }
    }
}
