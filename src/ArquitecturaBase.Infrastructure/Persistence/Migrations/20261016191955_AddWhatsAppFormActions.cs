using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFormActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OperatorUserId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "DispatchReservationId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentContentType",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentMediaId",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentName",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentSha256",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentSize",
                table: "WhatsAppOutgoingMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentStorageKey",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MessageEffectStartedAtUtc",
                table: "WhatsAppOutgoingMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Conversation");

            migrationBuilder.AddColumn<long>(
                name: "RecipientLinkRevision",
                table: "WhatsAppOutgoingMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientUserId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientWaId",
                table: "WhatsAppOutgoingMessages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Conversation");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_RequestId_RecipientUserId",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "RequestId", "RecipientUserId" },
                unique: true,
                filter: "\"Origin\" = 'FormAction'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema cannot represent system senders or redact form action bodies.
            // Preserve conversation operations and delivery receipts, and remove only the new action outbox.
            migrationBuilder.Sql("""
                DELETE FROM "WhatsAppOutgoingMessages" WHERE "Origin" = 'FormAction';
                UPDATE "WhatsAppMessages" SET "Body" = NULL WHERE "Origin" = 'FormAction';
                """);
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppOutgoingMessages_RequestId_RecipientUserId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DispatchReservationId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentContentType",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentMediaId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentName",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentSha256",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentSize",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DocumentStorageKey",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "MessageEffectStartedAtUtc",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "RecipientLinkRevision",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "RecipientWaId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "WhatsAppMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "OperatorUserId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
