using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordVoidReasonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords");

            migrationBuilder.AddColumn<string>(
                name: "VoidReasonCode",
                table: "BusinessFormRecords",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords",
                sql: "(\"State\" = 'Voided') = (\"VoidedByUserId\" IS NOT NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidedAtUtc\" IS NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidReason\" IS NULL AND \"VoidReasonCode\" IS NULL) AND (\"VoidReason\" IS NULL OR \"VoidReasonCode\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema needs a text in every void: refuse a void the system decided, with only the code of its reason,
            // instead of inventing a text. Before the DDL, so a refused Down changes nothing.
            migrationBuilder.Sql("""
                LOCK TABLE "BusinessFormRecords" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BusinessFormRecords" WHERE "VoidReasonCode" IS NOT NULL) THEN
                        RAISE EXCEPTION 'A void with only the code of its reason cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords");

            migrationBuilder.DropColumn(
                name: "VoidReasonCode",
                table: "BusinessFormRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormRecords_Void",
                table: "BusinessFormRecords",
                sql: "(\"State\" = 'Voided') = (\"VoidedByUserId\" IS NOT NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidedAtUtc\" IS NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidReason\" IS NULL)");
        }
    }
}
