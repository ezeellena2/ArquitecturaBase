using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaCertificateAndInvoicingSettingsAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "ArcaInvoicingSettings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "ArcaInvoicingSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "ArcaInvoicingSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                table: "ArcaInvoicingSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "ArcaCertificates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "ArcaCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "ArcaCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                table: "ArcaCertificates",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "ArcaInvoicingSettings");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ArcaInvoicingSettings");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "ArcaInvoicingSettings");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "ArcaInvoicingSettings");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "ArcaCertificates");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ArcaCertificates");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "ArcaCertificates");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "ArcaCertificates");
        }
    }
}
