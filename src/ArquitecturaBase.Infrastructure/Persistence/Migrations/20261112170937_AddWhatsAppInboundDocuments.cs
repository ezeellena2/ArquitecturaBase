using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInboundDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentActorUserId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentContentExpiresAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentContentType",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentContextJson",
                table: "WhatsAppMessages",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentCopySha256",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentFailureCode",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentFailureCount",
                table: "WhatsAppMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DocumentFileName",
                table: "WhatsAppMessages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentFirstFailureAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentLinkRevision",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentMediaId",
                table: "WhatsAppMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentPages",
                table: "WhatsAppMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentReadingJson",
                table: "WhatsAppMessages",
                type: "character varying(131072)",
                maxLength: 131072,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentRequestAwaitedUntilUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentRequestId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentSha256",
                table: "WhatsAppMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentSize",
                table: "WhatsAppMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentState",
                table: "WhatsAppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentStorageKey",
                table: "WhatsAppMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "FormReceivedDocuments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "FormReceivedDocuments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Sha256",
                table: "FormReceivedDocuments",
                type: "character(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_DocumentRequestId",
                table: "WhatsAppMessages",
                column: "DocumentRequestId",
                unique: true,
                filter: "\"DocumentRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PendingDocuments",
                table: "WhatsAppMessages",
                column: "ContactId",
                filter: "\"DocumentState\" IN ('Pending', 'Reading')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous inbox keeps every document with its copy: one kept without it (a file that could not be opened) cannot go back,
            // so the Down refuses before any DDL instead of deleting it.
            migrationBuilder.Sql("""
                LOCK TABLE "FormReceivedDocuments" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FormReceivedDocuments" WHERE "StorageKey" IS NULL OR "Sha256" IS NULL OR "Size" IS NULL) THEN
                        RAISE EXCEPTION 'Received documents kept without their file cannot be represented by the previous schema.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            // The previous image does not know a received document (EF cannot read an enum name it does not have): it goes back to the
            // media it was, with its caption, as a spreadsheet's Down does. Its copy is no longer referenced and the private file cleanup
            // takes it; what the inbox kept of it is the inbox's.
            migrationBuilder.Sql("UPDATE \"WhatsAppMessages\" SET \"Kind\" = 'Media' WHERE \"Kind\" = 'ReceivedDocument';");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_DocumentRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_PendingDocuments",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentActorUserId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentContentExpiresAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentContentType",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentContextJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentCopySha256",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentFailureCode",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentFailureCount",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentFileName",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentFirstFailureAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentLinkRevision",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentMediaId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentPages",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentReadingJson",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentRequestAwaitedUntilUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentSha256",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentSize",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentState",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "DocumentStorageKey",
                table: "WhatsAppMessages");

            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "FormReceivedDocuments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "FormReceivedDocuments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sha256",
                table: "FormReceivedDocuments",
                type: "character(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
