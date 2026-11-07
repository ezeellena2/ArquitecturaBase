using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordAndLoginAuditIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoginAudits_UserId",
                table: "LoginAudits");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAudits_UserId_OccurredAtUtc",
                table: "LoginAudits",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_Current_DefinitionId_Number",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "Number" },
                filter: "\"ReplacedByRecordId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_ActorUserId_Number",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "ActorUserId", "Number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoginAudits_UserId_OccurredAtUtc",
                table: "LoginAudits");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_Current_DefinitionId_Number",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_DefinitionId_ActorUserId_Number",
                table: "BusinessFormRecords");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAudits_UserId",
                table: "LoginAudits",
                column: "UserId");
        }
    }
}
