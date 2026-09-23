using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoginCodeDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "LoginCodes",
                newName: "Destination");

            migrationBuilder.RenameIndex(
                name: "IX_LoginCodes_Email_CreatedAtUtc",
                table: "LoginCodes",
                newName: "IX_LoginCodes_Destination_CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "LoginAudits",
                newName: "Identifier");

            // Hasta acá todos los códigos eran de ingreso por correo: es el valor por defecto de las dos columnas.
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "LoginCodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "LoginCodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SignIn");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "LoginCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtUtc",
                table: "LoginCodes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "LoginCodes");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "LoginCodes");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "LoginCodes");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "LoginCodes");

            migrationBuilder.RenameColumn(
                name: "Destination",
                table: "LoginCodes",
                newName: "Email");

            migrationBuilder.RenameIndex(
                name: "IX_LoginCodes_Destination_CreatedAtUtc",
                table: "LoginCodes",
                newName: "IX_LoginCodes_Email_CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "Identifier",
                table: "LoginAudits",
                newName: "Email");
        }
    }
}
