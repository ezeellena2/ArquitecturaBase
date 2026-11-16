using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordCatalogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormRecordCatalogFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameJson = table.Column<string>(type: "jsonb", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    RecordNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecordCatalogFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordCatalogFields_BusinessFormCatalogs_Catalo~",
                        column: x => x.CatalogId,
                        principalTable: "BusinessFormCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordCatalogFields_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordCatalogFields_CatalogId",
                table: "BusinessFormRecordCatalogFields",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordCatalogFields_DefinitionId_CatalogId_Fiel~",
                table: "BusinessFormRecordCatalogFields",
                columns: new[] { "DefinitionId", "CatalogId", "FieldId" },
                unique: true);

            // The union of the fields the existing responses sealed, as the table read them: per form, catalog and field the
            // one of the response with the highest number (and, on a tie, the latest reference).
            migrationBuilder.Sql("""
                INSERT INTO "BusinessFormRecordCatalogFields" ("Id", "DefinitionId", "CatalogId", "FieldId", "Key", "NameJson", "Type", "Role", "Position", "RecordNumber")
                SELECT DISTINCT ON (reference."DefinitionId", reference."CatalogId", entry.value ->> 'id')
                    gen_random_uuid(), reference."DefinitionId", reference."CatalogId", (entry.value ->> 'id')::uuid,
                    entry.value ->> 'key', entry.value -> 'name', entry.value ->> 'type', entry.value ->> 'role',
                    (entry.position - 1)::integer, record."Number"
                FROM "BusinessFormRecordReferences" AS reference
                JOIN "BusinessFormRecords" AS record ON record."Id" = reference."RecordId" AND record."DefinitionId" = reference."DefinitionId"
                CROSS JOIN LATERAL jsonb_array_elements(reference."FieldValuesJson") WITH ORDINALITY AS entry(value, position)
                WHERE reference."FieldValuesJson" IS NOT NULL
                ORDER BY reference."DefinitionId", reference."CatalogId", entry.value ->> 'id', record."Number" DESC, reference."Id" DESC
                """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormRecordCatalogFields");
        }
    }
}
