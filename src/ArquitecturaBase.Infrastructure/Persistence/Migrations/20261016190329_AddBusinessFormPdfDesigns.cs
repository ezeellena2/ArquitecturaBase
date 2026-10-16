using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormPdfDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActivePdfDesignId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessFormPdfDesigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    OriginalCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DesignJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormPdfDesigns", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormPdfDesigns_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormPdfDesigns_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Id_ActivePdfDesignId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActivePdfDesignId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormDocuments_DefinitionId_DesignId",
                table: "BusinessFormDocuments",
                columns: new[] { "DefinitionId", "DesignId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPdfDesigns_DefinitionId_Revision",
                table: "BusinessFormPdfDesigns",
                columns: new[] { "DefinitionId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessFormDocuments_BusinessFormPdfDesigns_DefinitionId_D~",
                table: "BusinessFormDocuments",
                columns: new[] { "DefinitionId", "DesignId" },
                principalTable: "BusinessFormPdfDesigns",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FormDefinitions_BusinessFormPdfDesigns_Id_ActivePdfDesignId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActivePdfDesignId" },
                principalTable: "BusinessFormPdfDesigns",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessFormDocuments_BusinessFormPdfDesigns_DefinitionId_D~",
                table: "BusinessFormDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_FormDefinitions_BusinessFormPdfDesigns_Id_ActivePdfDesignId",
                table: "FormDefinitions");

            migrationBuilder.DropTable(
                name: "BusinessFormPdfDesigns");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Id_ActivePdfDesignId",
                table: "FormDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_BusinessFormDocuments_DefinitionId_DesignId",
                table: "BusinessFormDocuments");

            migrationBuilder.DropColumn(
                name: "ActivePdfDesignId",
                table: "FormDefinitions");
        }
    }
}
