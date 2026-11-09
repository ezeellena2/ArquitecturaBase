using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantTurnAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentKey",
                table: "AiAssistantTurns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedAttachment",
                table: "AiAssistantTurns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_AttachmentKey",
                table: "AiAssistantTurns",
                column: "AttachmentKey",
                filter: "\"AttachmentKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_AttachmentKey",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "AttachmentKey",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "ProtectedAttachment",
                table: "AiAssistantTurns");
        }
    }
}
