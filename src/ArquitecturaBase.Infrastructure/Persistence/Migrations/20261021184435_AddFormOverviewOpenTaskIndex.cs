using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormOverviewOpenTaskIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_DefinitionId_RoleId_RequestedAtUtc_Id",
                table: "BusinessFormTasks",
                columns: new[] { "DefinitionId", "RoleId", "RequestedAtUtc", "Id" },
                filter: "\"State\" = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormTasks_DefinitionId_RoleId_RequestedAtUtc_Id",
                table: "BusinessFormTasks");
        }
    }
}
