using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingAndRetentionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppName",
                table: "SystemSettings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Acceso");

            migrationBuilder.AddColumn<string>(
                name: "EmailLogoUrl",
                table: "SystemSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderName",
                table: "SystemSettings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Acceso");

            migrationBuilder.AddColumn<int>(
                name: "InvitationRetentionDays",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "LoginAuditRetentionMonths",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "LoginCodeRetentionDays",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "ConversationRetentionDays",
                table: "AiConnections",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "ConversationRetentionDays",
                table: "AiConnectionChanges",
                type: "integer",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EmailLogoUrl",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EmailSenderName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "InvitationRetentionDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LoginAuditRetentionMonths",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LoginCodeRetentionDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ConversationRetentionDays",
                table: "AiConnections");

            migrationBuilder.DropColumn(
                name: "ConversationRetentionDays",
                table: "AiConnectionChanges");
        }
    }
}
