using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDefinitionStandard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StandardBindingsJson",
                table: "FormDefinitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandardKey",
                table: "FormDefinitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StandardVersion",
                table: "FormDefinitions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_StandardKey",
                table: "FormDefinitions",
                column: "StandardKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_StandardKey",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "StandardBindingsJson",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "StandardKey",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "StandardVersion",
                table: "FormDefinitions");
        }
    }
}
