using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFlowLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "WhatsAppMessages",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "WhatsAppMessages",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowBody",
                table: "WhatsAppFlowSessions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LocationLatitude",
                table: "WhatsAppFlowSessions",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LocationLongitude",
                table: "WhatsAppFlowSessions",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LocationRequired",
                table: "WhatsAppFlowSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationResolvedAtUtc",
                table: "WhatsAppFlowSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationState",
                table: "WhatsAppFlowSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "LocationWaMessageId",
                table: "WhatsAppFlowSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_ContactId_ExpiresAtUtc",
                table: "WhatsAppFlowSessions",
                columns: new[] { "ContactId", "ExpiresAtUtc" },
                filter: "\"LocationState\" = 'Requested' AND \"CaptureState\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppFlowSessions_ContactId_ExpiresAtUtc",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "FlowBody",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationLatitude",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationLongitude",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationRequired",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationResolvedAtUtc",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationState",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "LocationWaMessageId",
                table: "WhatsAppFlowSessions");
        }
    }
}
