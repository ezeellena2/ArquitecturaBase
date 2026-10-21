using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFlowDeprecation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeprecatedAtUtc",
                table: "WhatsAppFlowBindings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeprecationFence",
                table: "WhatsAppFlowBindings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "DeprecationInFlight",
                table: "WhatsAppFlowBindings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeprecationLeaseExpiresAtUtc",
                table: "WhatsAppFlowBindings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeprecationNextCheckAtUtc",
                table: "WhatsAppFlowBindings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecationState",
                table: "WhatsAppFlowBindings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowBindings_DeprecationNextCheckAtUtc_Id",
                table: "WhatsAppFlowBindings",
                columns: new[] { "DeprecationNextCheckAtUtc", "Id" },
                filter: "\"State\" = 'Published' AND \"DeprecationState\" <> 'Deprecated'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppFlowBindings_DeprecationNextCheckAtUtc_Id",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecatedAtUtc",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecationFence",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecationInFlight",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecationLeaseExpiresAtUtc",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecationNextCheckAtUtc",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "DeprecationState",
                table: "WhatsAppFlowBindings");
        }
    }
}
