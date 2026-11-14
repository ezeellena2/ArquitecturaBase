using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUncertainSendsAndNextAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DispatchRetries",
                table: "WhatsAppOutgoingMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "WhatsAppOutgoingMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendUncertain",
                table: "UserInvitations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchRetries",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "SendUncertain",
                table: "UserInvitations");
        }
    }
}
