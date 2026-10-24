using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCatalogItemsWithoutSpanishText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An option of an English original catalog may have no Spanish text. The previous image always writes it.
            migrationBuilder.AlterColumn<string>(
                name: "TextEs",
                table: "BusinessFormCatalogItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema needs a Spanish text in every option: refuse instead of inventing one. No row is
            // filled, so the column keeps the schema it had, without a default.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormCatalogItems" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormCatalogItems" WHERE "TextEs" IS NULL) THEN
                        RAISE EXCEPTION 'A catalog option without Spanish text cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.AlterColumn<string>(
                name: "TextEs",
                table: "BusinessFormCatalogItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
