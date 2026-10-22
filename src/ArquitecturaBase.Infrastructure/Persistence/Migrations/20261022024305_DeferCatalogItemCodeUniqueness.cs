using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeferCatalogItemCodeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormCatalogItems_CatalogId_Code",
                table: "BusinessFormCatalogItems");

            // EF cannot model deferrability. A transactional edit must be able to swap two item codes.
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessFormCatalogItems"
                ADD CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Code"
                UNIQUE ("CatalogId", "Code") DEFERRABLE INITIALLY DEFERRED;
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessFormCatalogItems"
                DROP CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Code";
                """.ReplaceLineEndings("\n"));

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormCatalogItems_CatalogId_Code",
                table: "BusinessFormCatalogItems",
                columns: new[] { "CatalogId", "Code" },
                unique: true);
        }
    }
}
