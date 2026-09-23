using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WaId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UserIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProfileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastInboundAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppContacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WaMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ReplyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StatusAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_UserId",
                table: "WhatsAppContacts",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_UserIdentifier",
                table: "WhatsAppContacts",
                column: "UserIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_WaId",
                table: "WhatsAppContacts",
                column: "WaId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ContactId",
                table: "WhatsAppMessages",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_WaMessageId",
                table: "WhatsAppMessages",
                column: "WaMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppContacts");
        }
    }
}
