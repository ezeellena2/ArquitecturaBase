using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MenuPlacement = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MenuIcon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFolders", x => x.Id);
                    table.CheckConstraint("CK_FormFolders_MenuIcon", "(\"MenuPlacement\" = 'Own') = (\"MenuIcon\" IS NOT NULL)");
                    table.CheckConstraint("CK_FormFolders_MenuPlacement", "\"MenuPlacement\" IN ('None', 'Own', 'Forms', 'Administration')");
                    table.ForeignKey(
                        name: "FK_FormFolders_FormFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FormFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormFolderMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFolderMemberships", x => x.Id);
                    table.CheckConstraint("CK_FormFolderMemberships_ItemKind", "\"ItemKind\" IN ('Form', 'Catalog')");
                    table.ForeignKey(
                        name: "FK_FormFolderMemberships_FormFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "FormFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormFolderMemberships_FolderId_ItemKind_ItemId",
                table: "FormFolderMemberships",
                columns: new[] { "FolderId", "ItemKind", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormFolderMemberships_ItemKind_ItemId",
                table: "FormFolderMemberships",
                columns: new[] { "ItemKind", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormFolders_ParentId_NormalizedName",
                table: "FormFolders",
                columns: new[] { "ParentId", "NormalizedName" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FormFolders_SystemKey",
                table: "FormFolders",
                column: "SystemKey",
                unique: true,
                filter: "\"SystemKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormFolderMemberships");

            migrationBuilder.DropTable(
                name: "FormFolders");
        }
    }
}
