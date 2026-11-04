using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAssistantProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs");

            migrationBuilder.AddColumn<string>(
                name: "ProposalEvidenceJson",
                table: "FormDefinitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_ProposalId",
                table: "FormDefinitions",
                column: "ProposalId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormDefinitions_Proposal",
                table: "FormDefinitions",
                sql: "(\"ProposalId\" IS NULL) = (\"ProposalEvidenceJson\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs",
                sql: "(\"ProposalId\" IS NULL AND \"ProposalEvidenceJson\" IS NULL AND \"ProposalState\" IS NULL AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL) OR (\"ProposalId\" IS NOT NULL AND \"ProposalEvidenceJson\" IS NOT NULL AND \"ProposalState\" IS NOT NULL AND ((\"ProposalState\" = 'Pending' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL) OR (\"ProposalState\" = 'Approved' AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL) OR (\"ProposalState\" = 'Discarded' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_ProposalId",
                table: "FormDefinitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormDefinitions_Proposal",
                table: "FormDefinitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "ProposalEvidenceJson",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "ProposalId",
                table: "FormDefinitions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_Proposal",
                table: "BusinessFormCatalogs",
                sql: "(\"ProposalId\" IS NULL AND \"ProposalEvidenceJson\" IS NULL AND \"ProposalState\" IS NULL\n    AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL)\nOR (\"Kind\" = 'Filters' AND \"ProposalId\" IS NOT NULL AND \"ProposalEvidenceJson\" IS NOT NULL AND \"ProposalState\" IS NOT NULL AND (\n    (\"ProposalState\" = 'Pending' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NULL AND \"ProposalDecidedAtUtc\" IS NULL)\n    OR (\"ProposalState\" = 'Approved' AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL)\n    OR (\"ProposalState\" = 'Discarded' AND NOT \"IsActive\" AND \"ProposalDecidedByUserId\" IS NOT NULL AND \"ProposalDecidedAtUtc\" IS NOT NULL)))");
        }
    }
}
