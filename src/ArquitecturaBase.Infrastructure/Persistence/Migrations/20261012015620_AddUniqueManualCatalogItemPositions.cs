using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueManualCatalogItemPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's PostgreSQL provider does not model deferrability. Defer this key so a transactional reorder can swap positions.
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessFormCatalogItems"
                ADD CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Position"
                UNIQUE ("CatalogId", "Position") DEFERRABLE INITIALLY DEFERRED;
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessFormCatalogItems"
                DROP CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Position";
                """.ReplaceLineEndings("\n"));
        }
    }
}
