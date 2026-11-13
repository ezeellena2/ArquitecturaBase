using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureReceiptRejectionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FixedValidation",
                table: "BusinessFormCaptureReceipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepeatedAtUtc",
                table: "BusinessFormCaptureReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepeatedRecordNumber",
                table: "BusinessFormCaptureReceipts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FixedValidation",
                table: "BusinessFormCaptureReceipts");

            migrationBuilder.DropColumn(
                name: "RepeatedAtUtc",
                table: "BusinessFormCaptureReceipts");

            migrationBuilder.DropColumn(
                name: "RepeatedRecordNumber",
                table: "BusinessFormCaptureReceipts");
        }
    }
}
