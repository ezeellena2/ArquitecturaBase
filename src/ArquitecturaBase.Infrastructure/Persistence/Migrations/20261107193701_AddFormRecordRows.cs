using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Position",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_ItemId",
                table: "BusinessFormRecordReferences");

            migrationBuilder.AddColumn<int>(
                name: "Row",
                table: "BusinessFormRecordValues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Row",
                table: "BusinessFormRecordReferences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedRow",
                table: "BusinessFormCaptureReceipts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Row_Position",
                table: "BusinessFormRecordValues",
                columns: new[] { "RecordId", "FieldId", "Row", "Position" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecordValues_Row",
                table: "BusinessFormRecordValues",
                sql: "\"Row\" >= 0 AND \"Row\" < 100");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_Row_ItemId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "RecordId", "FieldId", "Row", "ItemId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecordReferences_Row",
                table: "BusinessFormRecordReferences",
                sql: "\"Row\" >= 0 AND \"Row\" < 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCaptureReceipts_RejectedRow",
                table: "BusinessFormCaptureReceipts",
                sql: "\"RejectedRow\" IS NULL OR \"RejectedFieldId\" IS NOT NULL AND \"RejectedRow\" >= 0 AND \"RejectedRow\" < 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An earlier image cannot read the items of a block: a response of a version with a block (even without items), an
            // item above the first or a rejected item. Blocks in drafts and versions already existed there and do not stop it.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormRecords", "BusinessFormRecordValues", "BusinessFormRecordReferences", "BusinessFormCaptureReceipts" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormRecords" AS record
                            JOIN "BusinessFormVersions" AS version ON version."Id" = record."VersionId" AND version."DefinitionId" = record."DefinitionId"
                            WHERE jsonb_path_exists(version."SchemaJson", '$.questions[*] ? (@.type == "group")'))
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordValues" WHERE "Row" > 0)
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordReferences" WHERE "Row" > 0)
                        OR EXISTS (SELECT 1 FROM "BusinessFormCaptureReceipts" WHERE "RejectedRow" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Block items cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Row_Position",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecordValues_Row",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_Row_ItemId",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecordReferences_Row",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCaptureReceipts_RejectedRow",
                table: "BusinessFormCaptureReceipts");

            migrationBuilder.DropColumn(
                name: "Row",
                table: "BusinessFormRecordValues");

            migrationBuilder.DropColumn(
                name: "Row",
                table: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "RejectedRow",
                table: "BusinessFormCaptureReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordValues_RecordId_FieldId_Position",
                table: "BusinessFormRecordValues",
                columns: new[] { "RecordId", "FieldId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId_ItemId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "RecordId", "FieldId", "ItemId" },
                unique: true);
        }
    }
}
