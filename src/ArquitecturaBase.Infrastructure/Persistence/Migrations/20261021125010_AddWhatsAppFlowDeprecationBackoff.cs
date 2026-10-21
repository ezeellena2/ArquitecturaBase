using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFlowDeprecationBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeprecationFailureCount",
                table: "WhatsAppFlowBindings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeprecationFailureCount",
                table: "WhatsAppFlowBindings");
        }
    }
}
