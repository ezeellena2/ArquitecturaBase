using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormPersonData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormPersonAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameJson = table.Column<string>(type: "jsonb", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceCatalogId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormPersonAttributes", x => x.Id);
                    table.CheckConstraint("CK_BusinessFormPersonAttributes_Source", "(\"Type\" = 'Catalog') = (\"SourceCatalogId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BusinessFormPersonAttributes_BusinessFormCatalogs_SourceCat~",
                        column: x => x.SourceCatalogId,
                        principalTable: "BusinessFormCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormPersonData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormPersonData", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPersonAttributes_Key",
                table: "BusinessFormPersonAttributes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPersonAttributes_SourceCatalogId",
                table: "BusinessFormPersonAttributes",
                column: "SourceCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPersonData_UserId",
                table: "BusinessFormPersonData",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormPersonAttributes", "BusinessFormPersonData" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormPersonAttributes") OR EXISTS (SELECT 1 FROM "BusinessFormPersonData") THEN
                        RAISE EXCEPTION 'Person attributes and values cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropTable(
                name: "BusinessFormPersonAttributes");

            migrationBuilder.DropTable(
                name: "BusinessFormPersonData");
        }
    }
}
