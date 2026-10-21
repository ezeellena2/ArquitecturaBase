using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormLogicalDeletionAndSessionVersionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "FormDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FormDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_VersionId_ExpiresAtUtc",
                table: "WhatsAppFlowSessions",
                columns: new[] { "VersionId", "ExpiresAtUtc" },
                filter: "\"CaptureState\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppFlowSessions_VersionId_ExpiresAtUtc",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FormDefinitions");
        }
    }
}
