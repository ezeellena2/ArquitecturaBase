using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A correction is another record with the same number: the number stays unique among the responses as they
            // arrived, and the files a correction keeps point to the same stored file. Nothing existing is rewritten.
            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_DefinitionId_Number",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordFiles_FileId",
                table: "BusinessFormRecordFiles");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrectedByUserId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "BusinessFormRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalReceiptId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByRecordId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesRecordId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "BusinessFormRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "BusinessFormRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "BusinessFormRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_Number",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "Number" },
                unique: true,
                filter: "\"ReplacesRecordId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_ReplacedByRecordId",
                table: "BusinessFormRecords",
                column: "ReplacedByRecordId",
                unique: true,
                filter: "\"ReplacedByRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_ReplacesRecordId",
                table: "BusinessFormRecords",
                column: "ReplacesRecordId",
                unique: true,
                filter: "\"ReplacesRecordId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecords_Correction",
                table: "BusinessFormRecords",
                sql: "(\"ReplacesRecordId\" IS NULL) = (\"OriginalReceiptId\" IS NULL) AND (\"ReplacesRecordId\" IS NULL) = (\"CorrectedByUserId\" IS NULL) AND (\"ReplacesRecordId\" IS NULL) = (\"CorrectionReason\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords",
                sql: "(\"State\" = 'Voided') = (\"VoidedByUserId\" IS NOT NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidedAtUtc\" IS NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidReason\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_FileId_RecordId",
                table: "BusinessFormRecordFiles",
                columns: new[] { "FileId", "RecordId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_ReplacedByRecordId",
                table: "BusinessFormRecords",
                column: "ReplacedByRecordId",
                principalTable: "BusinessFormRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_ReplacesRecordId",
                table: "BusinessFormRecords",
                column: "ReplacesRecordId",
                principalTable: "BusinessFormRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema has no corrections, voided responses, closed tasks or shared files: refuse instead of
            // dropping what happened to a response.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormRecords", "BusinessFormRecordFiles", "BusinessFormTasks" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormRecords" WHERE "ReplacesRecordId" IS NOT NULL OR "State" = 'Voided')
                        OR EXISTS (SELECT 1 FROM "BusinessFormTasks" WHERE "State" = 'Closed')
                        OR EXISTS (SELECT 1 FROM "BusinessFormRecordFiles" GROUP BY "FileId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Corrected or voided responses cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_ReplacedByRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessFormRecords_BusinessFormRecords_ReplacesRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_DefinitionId_Number",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_ReplacedByRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecords_ReplacesRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecords_Correction",
                table: "BusinessFormRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormRecordFiles_FileId_RecordId",
                table: "BusinessFormRecordFiles");

            migrationBuilder.DropColumn(
                name: "CorrectedByUserId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "OriginalReceiptId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "ReplacedByRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "ReplacesRecordId",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "BusinessFormRecords");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecords_DefinitionId_Number",
                table: "BusinessFormRecords",
                columns: new[] { "DefinitionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_FileId",
                table: "BusinessFormRecordFiles",
                column: "FileId",
                unique: true);
        }
    }
}
