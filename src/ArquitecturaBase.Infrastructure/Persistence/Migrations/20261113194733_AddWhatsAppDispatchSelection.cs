using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppDispatchSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppOutgoingMessages_State_RequestedAtUtc_Id",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.AddColumn<bool>(
                name: "IsNotice",
                table: "WhatsAppOutgoingMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDispatchAtUtc",
                table: "WhatsAppContacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_State_IsNotice_RequestedAtUtc_Id",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "State", "IsNotice", "RequestedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppOutgoingMessages_State_IsNotice_RequestedAtUtc_Id",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "IsNotice",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "LastDispatchAtUtc",
                table: "WhatsAppContacts");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutgoingMessages_State_RequestedAtUtc_Id",
                table: "WhatsAppOutgoingMessages",
                columns: new[] { "State", "RequestedAtUtc", "Id" });
        }
    }
}
