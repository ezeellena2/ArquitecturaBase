using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantHomeAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AudioRequestId",
                table: "AiAssistantTurns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedTranscript",
                table: "AiAssistantTurns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_AudioRequestId",
                table: "AiAssistantTurns",
                column: "AudioRequestId",
                filter: "\"AudioRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_AudioRequestId",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "AudioRequestId",
                table: "AiAssistantTurns");

            migrationBuilder.DropColumn(
                name: "ProtectedTranscript",
                table: "AiAssistantTurns");
        }
    }
}
