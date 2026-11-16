using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Identifier",
                table: "LoginAudits",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_Channel_SentAtUtc",
                table: "LoginCodes",
                columns: new[] { "Channel", "SentAtUtc" },
                filter: "\"SentAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_RequestedByUserId_Channel_SentAtUtc",
                table: "LoginCodes",
                columns: new[] { "RequestedByUserId", "Channel", "SentAtUtc" },
                filter: "\"RequestedByUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAudits_OccurredAtUtc_RequestInfo",
                table: "LoginAudits",
                column: "OccurredAtUtc",
                filter: "\"IpAddress\" IS NOT NULL OR \"UserAgent\" IS NOT NULL OR (NOT \"Succeeded\" AND \"Identifier\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoginCodes_Channel_SentAtUtc",
                table: "LoginCodes");

            migrationBuilder.DropIndex(
                name: "IX_LoginCodes_RequestedByUserId_Channel_SentAtUtc",
                table: "LoginCodes");

            migrationBuilder.DropIndex(
                name: "IX_LoginAudits_OccurredAtUtc_RequestInfo",
                table: "LoginAudits");

            migrationBuilder.AlterColumn<string>(
                name: "Identifier",
                table: "LoginAudits",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);
        }
    }
}
