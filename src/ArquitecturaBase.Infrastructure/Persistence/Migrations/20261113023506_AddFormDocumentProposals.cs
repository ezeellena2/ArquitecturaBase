using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDocumentProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormDocumentProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Culture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    RereadVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingRereadIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    RereadAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RereadDay = table.Column<DateOnly>(type: "date", nullable: true),
                    RereadsOnDay = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDocumentProposals", x => x.Id);
                    table.CheckConstraint("CK_FormDocumentProposals_Reread", "\"RereadVersionId\" IS NULL OR \"DefinitionId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_FormDocumentProposals_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDocumentProposals_ActorUserId_RequestId",
                table: "FormDocumentProposals",
                columns: new[] { "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDocumentProposals_DefinitionId",
                table: "FormDocumentProposals",
                column: "DefinitionId",
                filter: "\"DefinitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormDocumentProposals_TopicId",
                table: "FormDocumentProposals",
                column: "TopicId",
                filter: "\"TopicId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Written by hand: the previous schema has no place for a request of «Armar formulario con la IA», so Down refuses one before
            // dropping the table instead of losing it.
            migrationBuilder.Sql("""
                LOCK TABLE "FormDocumentProposals" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FormDocumentProposals") THEN
                        RAISE EXCEPTION 'Form proposals from received documents cannot be represented by the previous schema.';
                    END IF;
                END $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropTable(
                name: "FormDocumentProposals");
        }
    }
}
