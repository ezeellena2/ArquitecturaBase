using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppAssistantAttachmentMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttachmentMessageId",
                table: "WhatsAppAssistantRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppAssistantRequests_Attachment",
                table: "WhatsAppAssistantRequests",
                sql: "\"AttachmentMessageId\" IS NULL OR \"SourceRecordId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppAssistantRequests_Attachment",
                table: "WhatsAppAssistantRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentMessageId",
                table: "WhatsAppAssistantRequests");
        }
    }
}
