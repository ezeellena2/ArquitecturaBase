using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormCatalogCreationRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "BusinessFormCatalogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogs_CreatedBy_CreationRequestId",
                table: "BusinessFormCatalogs",
                columns: new[] { "CreatedBy", "CreationRequestId" },
                unique: true,
                filter: "\"CreationRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormCatalogs_CreatedBy_CreationRequestId",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "BusinessFormCatalogs");
        }
    }
}
