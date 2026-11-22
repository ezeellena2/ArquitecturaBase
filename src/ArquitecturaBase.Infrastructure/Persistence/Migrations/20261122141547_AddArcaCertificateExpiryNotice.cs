using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaCertificateExpiryNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CertificateNoticeDays",
                table: "ArcaConnections",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNoticeSentAtUtc",
                table: "ArcaCertificates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateNoticeDays",
                table: "ArcaConnections");

            migrationBuilder.DropColumn(
                name: "ExpiryNoticeSentAtUtc",
                table: "ArcaCertificates");
        }
    }
}
