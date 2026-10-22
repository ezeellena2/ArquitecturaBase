using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogImportMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastImportAtUtc",
                table: "BusinessFormCatalogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastImportSha256",
                table: "BusinessFormCatalogs",
                type: "character(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_LastImport",
                table: "BusinessFormCatalogs",
                sql: "(\"LastImportSha256\" IS NULL) = (\"LastImportAtUtc\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_LastImport",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "LastImportAtUtc",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "LastImportSha256",
                table: "BusinessFormCatalogs");
        }
    }
}
