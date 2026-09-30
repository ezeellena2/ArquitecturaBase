using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCulture",
                table: "SystemSettings",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "es");

            migrationBuilder.AddColumn<int>(
                name: "DefaultPageSize",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "DefaultTimeZoneId",
                table: "SystemSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Argentina/Buenos_Aires");

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "SystemSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCulture",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPageSize",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "DefaultTimeZoneId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SystemSettings");
        }
    }
}
