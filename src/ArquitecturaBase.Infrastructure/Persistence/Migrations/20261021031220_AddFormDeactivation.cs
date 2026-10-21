using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDeactivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAtUtc",
                table: "FormDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeactivatedBy",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormDefinitions_Deactivation",
                table: "FormDefinitions",
                sql: "(\"DeactivatedAtUtc\" IS NULL) = (\"DeactivatedBy\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FormDefinitions_Deactivation",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                table: "FormDefinitions");
        }
    }
}
