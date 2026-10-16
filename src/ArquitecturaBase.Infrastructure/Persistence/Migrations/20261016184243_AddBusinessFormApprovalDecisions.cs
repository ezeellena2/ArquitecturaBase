using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormApprovalDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                table: "BusinessFormTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedByUserId",
                table: "BusinessFormTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "BusinessFormTasks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormTasks_RoleId_State_RequestedAtUtc_Id",
                table: "BusinessFormTasks",
                columns: new[] { "RoleId", "State", "RequestedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormTasks_RoleId_State_RequestedAtUtc_Id",
                table: "BusinessFormTasks");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "BusinessFormTasks");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "BusinessFormTasks");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "BusinessFormTasks");
        }
    }
}
