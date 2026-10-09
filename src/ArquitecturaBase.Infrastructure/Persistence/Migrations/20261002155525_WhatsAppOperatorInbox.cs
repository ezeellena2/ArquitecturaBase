using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppOperatorInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InboundSequence",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "OperatorUserId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastInboundSequence",
                table: "WhatsAppContacts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "WhatsAppContacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Bot");

            migrationBuilder.AddColumn<long>(
                name: "ModeRevision",
                table: "WhatsAppContacts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Preserve the order of stored inbound messages when introducing per-operator reading.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "ContactId" ORDER BY "OccurredAtUtc", "Id") AS sequence
                    FROM "WhatsAppMessages"
                    WHERE "Direction" = 'Inbound' AND "ContactId" IS NOT NULL
                )
                UPDATE "WhatsAppMessages" AS message
                SET "InboundSequence" = ranked.sequence
                FROM ranked WHERE message."Id" = ranked."Id";

                UPDATE "WhatsAppContacts" AS contact
                SET "LastInboundSequence" = (
                    SELECT COALESCE(MAX(message."InboundSequence"), 0)
                    FROM "WhatsAppMessages" AS message WHERE message."ContactId" = contact."Id"
                );
                """);

            migrationBuilder.CreateTable(
                name: "WhatsAppDeliveryReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WaMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppDeliveryReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutgoingMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WaMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutgoingMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutgoingMessages_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppReadStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReadSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppReadStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppReadStates_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ContactId_InboundSequence",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "InboundSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ContactId_OccurredAtUtc_Id",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppDeliveryReceipts_WaMessageId",
                table: "WhatsAppDeliveryReceipts",
                column: "WaMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_ContactId_OperatorUserId_RequestId",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "ContactId", "OperatorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_State_RequestedAtUtc_Id",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "State", "RequestedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppReadStates_ContactId_OperatorUserId",
                table: "WhatsAppReadStates",
                columns: new[] { "ContactId", "OperatorUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppDeliveryReceipts");

            migrationBuilder.DropTable(
                name: "WhatsAppOutgoingMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppReadStates");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ContactId_InboundSequence",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ContactId_OccurredAtUtc_Id",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "InboundSequence",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "OperatorUserId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "LastInboundSequence",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "ModeRevision",
                table: "WhatsAppContacts");
        }
    }
}
