using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDocumentReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentReadingJson",
                table: "FormDefinitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentReadingJson",
                table: "BusinessFormVersions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema has nowhere to keep the reading of documents: refuse a draft or a version that has one instead of
            // dropping what was written. Before the DDL, so a refused Down changes nothing.
            migrationBuilder.Sql("""
                LOCK TABLE "FormDefinitions", "BusinessFormVersions" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FormDefinitions" WHERE "DocumentReadingJson" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "BusinessFormVersions" WHERE "DocumentReadingJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'A form that reads documents cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropColumn(
                name: "DocumentReadingJson",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "DocumentReadingJson",
                table: "BusinessFormVersions");
        }
    }
}
