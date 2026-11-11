using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeferActiveRuleSetKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId",
                table: "FormDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Id_ActiveRuleSetId",
                table: "FormDefinitions");

            // EF cannot model deferrability, and with this relationship in the model it cannot save a new definition with its
            // first rule set active («Agregar» from the library): the two rows point at each other. The key stays, checked at commit.
            // NO ACTION, not RESTRICT: a RESTRICT check is never deferred. Rule sets are never deleted.
            migrationBuilder.Sql("""
                ALTER TABLE "FormDefinitions"
                ADD CONSTRAINT "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId"
                FOREIGN KEY ("Id", "ActiveRuleSetId") REFERENCES "BusinessFormRuleSets" ("DefinitionId", "Id")
                DEFERRABLE INITIALLY DEFERRED;
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "FormDefinitions"
                DROP CONSTRAINT "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId";
                """.ReplaceLineEndings("\n"));

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Id_ActiveRuleSetId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveRuleSetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveRuleSetId" },
                principalTable: "BusinessFormRuleSets",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
