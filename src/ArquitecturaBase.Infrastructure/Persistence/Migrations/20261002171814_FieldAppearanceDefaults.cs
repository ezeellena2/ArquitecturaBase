using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FieldAppearanceDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Promote only the untouched original palette. A single customized color preserves the entire group.
            // This technical data migration records a new revision and an unattributed UTC audit timestamp.
            migrationBuilder.Sql("""
                UPDATE "SystemSettings"
                SET "PrimaryColor" = '#285A3D', "MenuColor" = '#24352B',
                    "HeaderColor" = '#DFE8D9', "AlternateRowColor" = '#F4F6F0',
                    "Revision" = "Revision" + 1, "ModifiedAtUtc" = CURRENT_TIMESTAMP, "ModifiedBy" = NULL
                WHERE UPPER("PrimaryColor") = '#176B4C' AND UPPER("MenuColor") = '#194F3A'
                  AND UPPER("HeaderColor") = '#D0E5D5' AND UPPER("AlternateRowColor") = '#F2F7F3';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#285A3D",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#176B4C");

            migrationBuilder.AlterColumn<string>(
                name: "MenuColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#24352B",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#194F3A");

            migrationBuilder.AlterColumn<string>(
                name: "HeaderColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#DFE8D9",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#D0E5D5");

            migrationBuilder.AlterColumn<string>(
                name: "AlternateRowColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#F4F6F0",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#F2F7F3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The inverse also matches all four defaults, preserving any subsequent customized palette.
            migrationBuilder.Sql("""
                UPDATE "SystemSettings"
                SET "PrimaryColor" = '#176B4C', "MenuColor" = '#194F3A',
                    "HeaderColor" = '#D0E5D5', "AlternateRowColor" = '#F2F7F3',
                    "Revision" = "Revision" + 1, "ModifiedAtUtc" = CURRENT_TIMESTAMP, "ModifiedBy" = NULL
                WHERE UPPER("PrimaryColor") = '#285A3D' AND UPPER("MenuColor") = '#24352B'
                  AND UPPER("HeaderColor") = '#DFE8D9' AND UPPER("AlternateRowColor") = '#F4F6F0';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#176B4C",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#285A3D");

            migrationBuilder.AlterColumn<string>(
                name: "MenuColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#194F3A",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#24352B");

            migrationBuilder.AlterColumn<string>(
                name: "HeaderColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#D0E5D5",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#DFE8D9");

            migrationBuilder.AlterColumn<string>(
                name: "AlternateRowColor",
                table: "SystemSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#F2F7F3",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#F4F6F0");
        }
    }
}
