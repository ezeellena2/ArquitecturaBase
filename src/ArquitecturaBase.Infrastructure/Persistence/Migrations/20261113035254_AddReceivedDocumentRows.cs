using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivedDocumentRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormReceivedDocuments_ChannelId_SourceId",
                table: "FormReceivedDocuments");

            migrationBuilder.AddColumn<string>(
                name: "NotSentReason",
                table: "FormReceivedDocuments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowNumber",
                table: "FormReceivedDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RowKind",
                table: "FormReceivedDocuments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowOrder",
                table: "FormReceivedDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SheetName",
                table: "FormReceivedDocuments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_Sha256",
                table: "FormReceivedDocuments",
                column: "Sha256",
                filter: "\"RowNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_ChannelId_SourceId_SheetName_RowNumber",
                table: "FormReceivedDocuments",
                columns: new[] { "ChannelId", "SourceId", "SheetName", "RowNumber" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // A document kept for the team because its form could not be sent to its person says so.
            migrationBuilder.Sql("UPDATE \"FormReceivedDocuments\" SET \"NotSentReason\" = 'NotSendable' WHERE \"NotSendable\";");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormReceivedDocuments_NotSent",
                table: "FormReceivedDocuments",
                sql: "\"NotSendable\" = (\"NotSentReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormReceivedDocuments_Row",
                table: "FormReceivedDocuments",
                sql: "(\"RowNumber\" IS NULL OR \"RowNumber\" BETWEEN 1 AND 1048576) AND (\"SheetName\" IS NULL OR \"RowNumber\" IS NOT NULL) AND (\"NotSentReason\" IS DISTINCT FROM 'OverLimit' OR \"RowNumber\" IS NOT NULL) AND (\"RowNumber\" IS NULL) = (\"RowOrder\" IS NULL) AND (\"RowNumber\" IS NULL) = (\"RowKind\" IS NULL) AND (\"RowOrder\" IS NULL OR \"RowOrder\" >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema keeps one entry per channel source: a row of a spreadsheet cannot go back without losing it. Without rows,
            // why a document was not sent is the reason «NotSendable» keeps (an excess of loads is only a row's).
            migrationBuilder.Sql("""
                LOCK TABLE "FormReceivedDocuments" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FormReceivedDocuments" WHERE "RowNumber" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Rows of spreadsheets in the received documents cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropIndex(
                name: "IX_FormReceivedDocuments_ChannelId_SourceId_SheetName_RowNumber",
                table: "FormReceivedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_FormReceivedDocuments_Sha256",
                table: "FormReceivedDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormReceivedDocuments_NotSent",
                table: "FormReceivedDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormReceivedDocuments_Row",
                table: "FormReceivedDocuments");

            migrationBuilder.DropColumn(
                name: "NotSentReason",
                table: "FormReceivedDocuments");

            migrationBuilder.DropColumn(
                name: "RowKind",
                table: "FormReceivedDocuments");

            migrationBuilder.DropColumn(
                name: "RowNumber",
                table: "FormReceivedDocuments");

            migrationBuilder.DropColumn(
                name: "RowOrder",
                table: "FormReceivedDocuments");

            migrationBuilder.DropColumn(
                name: "SheetName",
                table: "FormReceivedDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_ChannelId_SourceId",
                table: "FormReceivedDocuments",
                columns: new[] { "ChannelId", "SourceId" },
                unique: true);
        }
    }
}
