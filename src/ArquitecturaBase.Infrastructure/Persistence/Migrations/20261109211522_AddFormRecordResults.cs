using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormRecordResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceNameJson = table.Column<string>(type: "jsonb", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecordResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordResults_BusinessFormActionRuns_RecordId_R~",
                        columns: x => new { x.RecordId, x.RunId },
                        principalTable: "BusinessFormActionRuns",
                        principalColumns: new[] { "RecordId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordResults_BusinessFormRecords_DefinitionId_~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordResults_DefinitionId_RecordId",
                table: "BusinessFormRecordResults",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordResults_DefinitionId_Source_RecordedAtUtc",
                table: "BusinessFormRecordResults",
                columns: new[] { "DefinitionId", "Source", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordResults_RecordId_RunId",
                table: "BusinessFormRecordResults",
                columns: new[] { "RecordId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordResults_RunId",
                table: "BusinessFormRecordResults",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormRecordResults");
        }
    }
}
