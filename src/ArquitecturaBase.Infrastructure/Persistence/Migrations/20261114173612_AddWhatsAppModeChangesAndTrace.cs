using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppModeChangesAndTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "WhatsAppMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppModeChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModeRevision = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppModeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppModeChanges_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppModeChanges_ContactId_ModeRevision",
                table: "WhatsAppModeChanges",
                columns: new[] { "ContactId", "ModeRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppModeChanges");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "WhatsAppMessages");
        }
    }
}
