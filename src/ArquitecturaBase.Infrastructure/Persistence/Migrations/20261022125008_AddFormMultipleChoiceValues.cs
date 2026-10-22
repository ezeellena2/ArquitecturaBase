using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormMultipleChoiceValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId",
                table: "BusinessFormRecordReferences");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "BusinessFormRecordValues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Position",
                table: "BusinessFormRecordValues",
                columns: new[] { "RecordId", "FieldId", "Position" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecordValues_Position",
                table: "BusinessFormRecordValues",
                sql: "\"Position\" >= 0 AND \"Position\" < 20");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_ItemId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "RecordId", "FieldId", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An earlier image cannot represent the new question type or multiple captured rows.
            migrationBuilder.Sql("""
                LOCK TABLE "FormDefinitions", "BusinessFormFields", "BusinessFormRecordValues", "BusinessFormRecordReferences" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FormDefinitions"
                        WHERE jsonb_path_exists("SchemaJson", '$.fields[*] ? (@.type == "multiChoice")'))
                        OR EXISTS (SELECT 1 FROM "BusinessFormFields" WHERE "Type" = 'multiChoice')
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordValues" WHERE "Position" > 0)
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordReferences" GROUP BY "RecordId", "FieldId" HAVING COUNT(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Multiple-choice facts cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Position",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecordValues_Position",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_ItemId",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "BusinessFormRecordValues");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId",
                table: "BusinessFormRecordValues",
                columns: new[] { "RecordId", "FieldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "RecordId", "FieldId" },
                unique: true);
        }
    }
}
