using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlternateRowColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#F2F7F3");

            migrationBuilder.AddColumn<string>(
                name: "HeaderColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#D0E5D5");

            migrationBuilder.AddColumn<string>(
                name: "MenuColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#194F3A");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#176B4C");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlternateRowColor",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "HeaderColor",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MenuColor",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "SystemSettings");
        }
    }
}
