using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormCatalogResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlowRejectedQuestion",
                table: "WhatsAppMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedQuestion",
                table: "WhatsAppFlowSessions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedFieldId",
                table: "BusinessFormCaptureReceipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessFormRecordReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogRevision = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CatalogName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecordReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordReferences_BusinessFormCatalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "BusinessFormCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordReferences_BusinessFormFields_DefinitionI~",
                        columns: x => new { x.DefinitionId, x.FieldId },
                        principalTable: "BusinessFormFields",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordReferences_BusinessFormRecords_Definition~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_CatalogId",
                table: "BusinessFormRecordReferences",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_DefinitionId_FieldId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "DefinitionId", "FieldId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_DefinitionId_RecordId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordReferences_RecordId_FieldId",
                table: "BusinessFormRecordReferences",
                columns: new[] { "RecordId", "FieldId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormRecordReferences");

            migrationBuilder.DropColumn(
                name: "FlowRejectedQuestion",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "RejectedQuestion",
                table: "WhatsAppFlowSessions");

            migrationBuilder.DropColumn(
                name: "RejectedFieldId",
                table: "BusinessFormCaptureReceipts");
        }
    }
}
