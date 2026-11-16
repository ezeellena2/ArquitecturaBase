using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginCodeRequestedFromAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestedFromAddress",
                table: "LoginCodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_RequestedFromAddress_CreatedAtUtc",
                table: "LoginCodes",
                columns: new[] { "RequestedFromAddress", "CreatedAtUtc" },
                filter: "\"RequestedFromAddress\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoginCodes_RequestedFromAddress_CreatedAtUtc",
                table: "LoginCodes");

            migrationBuilder.DropColumn(
                name: "RequestedFromAddress",
                table: "LoginCodes");
        }
    }
}
