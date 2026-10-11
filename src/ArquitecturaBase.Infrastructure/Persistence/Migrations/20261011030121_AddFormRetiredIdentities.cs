using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRetiredIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormRetiredIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRetiredIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormRetiredIdentities_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRetiredIdentities_DefinitionId_Kind_IdentityId",
                table: "BusinessFormRetiredIdentities",
                columns: new[] { "DefinitionId", "Kind", "IdentityId" },
                unique: true);

            // Preserve retired field and choice IDs while moving the unbounded JSON ledger into indexed rows.
            migrationBuilder.Sql("""
                INSERT INTO "BusinessFormRetiredIdentities"
                    ("Id", "DefinitionId", "IdentityId", "Kind", "CreatedAtUtc")
                SELECT uuidv7(), definition."Id", retired."IdentityId"::uuid, retired."Kind", now()
                FROM "FormDefinitions" AS definition
                CROSS JOIN LATERAL (
                    SELECT value AS "IdentityId", 'Field' AS "Kind"
                    FROM jsonb_array_elements_text(COALESCE(definition."RetiredIdentityLedgerJson" -> 'fields', '[]'::jsonb))
                    UNION ALL
                    SELECT value AS "IdentityId", 'Choice' AS "Kind"
                    FROM jsonb_array_elements_text(COALESCE(definition."RetiredIdentityLedgerJson" -> 'choices', '[]'::jsonb))
                ) AS retired;
                """);

            migrationBuilder.DropColumn(
                name: "RetiredIdentityLedgerJson",
                table: "FormDefinitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetiredIdentityLedgerJson",
                table: "FormDefinitions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.Sql("""
                UPDATE "FormDefinitions" AS definition
                SET "RetiredIdentityLedgerJson" = jsonb_build_object(
                    'fields', COALESCE((
                        SELECT jsonb_agg(identity."IdentityId"::text ORDER BY identity."IdentityId")
                        FROM "BusinessFormRetiredIdentities" AS identity
                        WHERE identity."DefinitionId" = definition."Id" AND identity."Kind" = 'Field'
                    ), '[]'::jsonb),
                    'choices', COALESCE((
                        SELECT jsonb_agg(identity."IdentityId"::text ORDER BY identity."IdentityId")
                        FROM "BusinessFormRetiredIdentities" AS identity
                        WHERE identity."DefinitionId" = definition."Id" AND identity."Kind" = 'Choice'
                    ), '[]'::jsonb)
                );
                """);

            migrationBuilder.DropTable(name: "BusinessFormRetiredIdentities");
        }
    }
}
