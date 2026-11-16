using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormActionRunStallNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StallNoticedAtUtc",
                table: "BusinessFormActionRuns",
                type: "timestamp with time zone",
                nullable: true);
            // F2: what was already stopped before this version is not announced now.
            migrationBuilder.Sql("UPDATE \"BusinessFormActionRuns\" SET \"StallNoticedAtUtc\" = now() WHERE \"State\" IN ('Failed', 'Uncertain');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StallNoticedAtUtc",
                table: "BusinessFormActionRuns");
        }
    }
}
