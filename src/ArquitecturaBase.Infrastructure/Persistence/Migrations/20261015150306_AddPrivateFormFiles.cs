using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateFormFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.CheckConstraint("CK_StoredFiles_Size", "\"Size\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormRecordFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormRecordFiles", x => x.Id);
                    table.CheckConstraint("CK_BusinessFormRecordFiles_Position", "\"Position\" >= 0 AND \"Position\" < 10");
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordFiles_BusinessFormFields_DefinitionId_Fie~",
                        columns: x => new { x.DefinitionId, x.FieldId },
                        principalTable: "BusinessFormFields",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordFiles_BusinessFormRecords_DefinitionId_Re~",
                        columns: x => new { x.DefinitionId, x.RecordId },
                        principalTable: "BusinessFormRecords",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessFormRecordFiles_StoredFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_DefinitionId_FieldId",
                table: "BusinessFormRecordFiles",
                columns: new[] { "DefinitionId", "FieldId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_DefinitionId_RecordId",
                table: "BusinessFormRecordFiles",
                columns: new[] { "DefinitionId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_FileId",
                table: "BusinessFormRecordFiles",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormRecordFiles_RecordId_FieldId_Position",
                table: "BusinessFormRecordFiles",
                columns: new[] { "RecordId", "FieldId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerUserId",
                table: "StoredFiles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_StorageKey",
                table: "StoredFiles",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormRecordFiles");

            migrationBuilder.DropTable(
                name: "StoredFiles");
        }
    }
}
