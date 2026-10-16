using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveRuleSetId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessFormRuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    OriginalCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    QuestionSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    HasApproval = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRuleSets", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormRuleSets_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormRuleSets_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Id_ActiveRuleSetId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveRuleSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRuleSets_DefinitionId_Revision",
                table: "BusinessFormRuleSets",
                columns: new[] { "DefinitionId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveRuleSetId" },
                principalTable: "BusinessFormRuleSets",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId",
                table: "FormDefinitions");

            migrationBuilder.DropTable(
                name: "BusinessFormRuleSets");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Id_ActiveRuleSetId",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "ActiveRuleSetId",
                table: "FormDefinitions");
        }
    }
}
