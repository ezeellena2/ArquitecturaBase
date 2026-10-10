using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveVersionId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessFormFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormFields_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    SchemaFormatVersion = table.Column<int>(type: "integer", nullable: false),
                    SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormVersions", x => x.Id);
                    table.UniqueConstraint("AK_BusinessFormVersions_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_BusinessFormVersions_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessFormPublicationOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    VersionNumber = table.Column<long>(type: "bigint", nullable: false),
                    BaseActiveVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedManifest = table.Column<byte[]>(type: "bytea", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormPublicationOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFormPublicationOperations_BusinessFormVersions_Defi~",
                        columns: x => new { x.DefinitionId, x.BaseActiveVersionId },
                        principalTable: "BusinessFormVersions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessFormPublicationOperations_BusinessFormVersions_Def~1",
                        columns: x => new { x.DefinitionId, x.VersionId },
                        principalTable: "BusinessFormVersions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Id_ActiveVersionId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormFields_DefinitionId_StableKey",
                table: "BusinessFormFields",
                columns: new[] { "DefinitionId", "StableKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPublicationOperations_DefinitionId",
                table: "BusinessFormPublicationOperations",
                column: "DefinitionId",
                unique: true,
                filter: "\"State\" NOT IN ('Published', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPublicationOperations_DefinitionId_BaseActiveVe~",
                table: "BusinessFormPublicationOperations",
                columns: new[] { "DefinitionId", "BaseActiveVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPublicationOperations_DefinitionId_VersionId",
                table: "BusinessFormPublicationOperations",
                columns: new[] { "DefinitionId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPublicationOperations_OperatorUserId_RequestId",
                table: "BusinessFormPublicationOperations",
                columns: new[] { "OperatorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormPublicationOperations_VersionId",
                table: "BusinessFormPublicationOperations",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFormVersions_DefinitionId_Number",
                table: "BusinessFormVersions",
                columns: new[] { "DefinitionId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FormDefinitions_BusinessFormVersions_Id_ActiveVersionId",
                table: "FormDefinitions",
                columns: new[] { "Id", "ActiveVersionId" },
                principalTable: "BusinessFormVersions",
                principalColumns: new[] { "DefinitionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormDefinitions_BusinessFormVersions_Id_ActiveVersionId",
                table: "FormDefinitions");

            migrationBuilder.DropTable(
                name: "BusinessFormFields");

            migrationBuilder.DropTable(
                name: "BusinessFormPublicationOperations");

            migrationBuilder.DropTable(
                name: "BusinessFormVersions");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Id_ActiveVersionId",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "ActiveVersionId",
                table: "FormDefinitions");
        }
    }
}
