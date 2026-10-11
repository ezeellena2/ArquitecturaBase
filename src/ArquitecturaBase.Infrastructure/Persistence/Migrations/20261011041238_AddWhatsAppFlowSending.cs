using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFlowSending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FlowSessionId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "WhatsAppOutgoingMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowSessionId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_WhatsAppFlowBindings_Id_VersionId",
                table: "WhatsAppFlowBindings",
                columns: new[] { "Id", "VersionId" });

            migrationBuilder.CreateTable(
                name: "WhatsAppFlowSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Culture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    NameJson = table.Column<string>(type: "jsonb", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppFlowSessions", x => x.Id);
                    table.UniqueConstraint("AK_WhatsAppFlowSessions_Id_ContactId_OperatorUserId", x => new { x.Id, x.ContactId, x.OperatorUserId });
                    table.ForeignKey(
                        name: "FK_WhatsAppFlowSessions_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppFlowSessions_WhatsAppFlowBindings_BindingId_Version~",
                        columns: x => new { x.BindingId, x.VersionId },
                        principalTable: "WhatsAppFlowBindings",
                        principalColumns: new[] { "Id", "VersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_FlowSessionId",
                table: "WhatsAppOutgoingMessages",
                column: "FlowSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_FlowSessionId",
                table: "WhatsAppMessages",
                column: "FlowSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_BindingId_VersionId",
                table: "WhatsAppFlowSessions",
                columns: new[] { "BindingId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_ContactId",
                table: "WhatsAppFlowSessions",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_ExpiresAtUtc_Id",
                table: "WhatsAppFlowSessions",
                columns: new[] { "ExpiresAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowSessions_TokenHash",
                table: "WhatsAppFlowSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppMessages",
                column: "FlowSessionId",
                principalTable: "WhatsAppFlowSessions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages",
                column: "FlowSessionId",
                principalTable: "WhatsAppFlowSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppOutgoingMessages_WhatsAppFlowSessions_FlowSessionId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppFlowSessions");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppOutgoingMessages_FlowSessionId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_FlowSessionId",
                table: "WhatsAppMessages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_WhatsAppFlowBindings_Id_VersionId",
                table: "WhatsAppFlowBindings");

            migrationBuilder.DropColumn(
                name: "FlowSessionId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "FlowSessionId",
                table: "WhatsAppMessages");
        }
    }
}
