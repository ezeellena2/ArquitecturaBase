using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandImagesAccentAndFont : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ME2e: EmailLogoUrl leaves the model now, but its column stays until the next deploy, because the previous image
            // still reads it while this bundle runs (guía de migración, «Un borrado no viaja con su reemplazo»). The drop
            // EF generated, and its inverse in Down, go in a later migration written by hand.
            migrationBuilder.AddColumn<string>(
                name: "Font",
                table: "SystemSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "public-sans");

            migrationBuilder.AddColumn<string>(
                name: "IconFileKey",
                table: "SystemSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoFileKey",
                table: "SystemSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MenuAccentColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Font",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "IconFileKey",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LogoFileKey",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MenuAccentColor",
                table: "SystemSettings");
        }
    }
}
