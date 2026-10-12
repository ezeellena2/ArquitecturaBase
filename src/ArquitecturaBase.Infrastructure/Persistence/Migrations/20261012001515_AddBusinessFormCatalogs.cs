using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormCatalogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NameJson = table.Column<string>(type: "jsonb", nullable: false),
                    DescriptionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TextEs = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TextEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DetailEs = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DetailEn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormCatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormCatalogItems_BusinessFormCatalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "BusinessFormCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogItems_CatalogId_Code",
                table: "BusinessFormCatalogItems",
                columns: new[] { "CatalogId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogItems_CatalogId_IsActive_Position_Id",
                table: "BusinessFormCatalogItems",
                columns: new[] { "CatalogId", "IsActive", "Position", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogs_Key",
                table: "BusinessFormCatalogs",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormCatalogItems");

            migrationBuilder.DropTable(
                name: "BusinessFormCatalogs");
        }
    }
}
