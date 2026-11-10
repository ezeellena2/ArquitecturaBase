using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInboundSpreadsheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetContentType",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetCopySha256",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetFailureCode",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpreadsheetFailureCount",
                table: "WhatsAppMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetFileName",
                table: "WhatsAppMessages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SpreadsheetFirstFailureAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetMediaId",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetPreparedJson",
                table: "WhatsAppMessages",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetSha256",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SpreadsheetSize",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetState",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetStorageKey",
                table: "WhatsAppMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingSpreadsheets",
                table: "WhatsAppMessages",
                column: "ContactId",
                filter: "\"SpreadsheetState\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous image does not know a received spreadsheet (EF cannot read an enum name it does not have): it goes back to
            // the media it was, with its caption. Its copy is no longer referenced and the private file cleanup takes it.
            migrationBuilder.Sql("UPDATE \"WhatsAppMessages\" SET \"Kind\" = 'Media' WHERE \"Kind\" = 'Spreadsheet';");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingSpreadsheets",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetContentType",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetCopySha256",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetFailureCode",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetFailureCount",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetFileName",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetFirstFailureAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetMediaId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetPreparedJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetSha256",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetSize",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetState",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SpreadsheetStorageKey",
                table: "WhatsAppMessages");
        }
    }
}
