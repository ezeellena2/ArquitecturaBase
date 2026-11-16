using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordReceivedDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_Current_DefinitionId_SubmittedAtUtc",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "SubmittedAtUtc" },
                filter: "\"ReplacedByRecordId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_Current_DefinitionId_SubmittedAtUtc",
                table: "BusinessFormRecords");
        }
    }
}
