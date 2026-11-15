using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormActionRunRequestsAndAutoConsult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoConsultCount",
                table: "BusinessFormActionRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastRequestKind",
                table: "BusinessFormActionRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRequestedAtUtc",
                table: "BusinessFormActionRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastRequestedByUserId",
                table: "BusinessFormActionRuns",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConsultCount",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "LastRequestKind",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "LastRequestedAtUtc",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "LastRequestedByUserId",
                table: "BusinessFormActionRuns");
        }
    }
}
