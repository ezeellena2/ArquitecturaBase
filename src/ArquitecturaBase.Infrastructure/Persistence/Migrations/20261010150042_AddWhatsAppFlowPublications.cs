using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFlowPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppFlowOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NeutralOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedManifest = table.Column<byte[]>(type: "bytea", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppFlowOperations", x => x.Id);
                    table.UniqueConstraint("AK_WhatsAppFlowOperations_Id_VersionId", x => new { x.Id, x.VersionId });
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppFlowBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Culture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    TechnicalName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProtectedProjection = table.Column<byte[]>(type: "bytea", nullable: false),
                    MetaFlowId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Step = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InFlight = table.Column<bool>(type: "boolean", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppFlowBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppFlowBindings_WhatsAppFlowOperations_OperationId_Ver~",
                        columns: x => new { x.OperationId, x.VersionId },
                        principalTable: "WhatsAppFlowOperations",
                        principalColumns: new[] { "Id", "VersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowBindings_MetaFlowId",
                table: "WhatsAppFlowBindings",
                column: "MetaFlowId",
                unique: true,
                filter: "\"MetaFlowId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowBindings_OperationId_VersionId",
                table: "WhatsAppFlowBindings",
                columns: new[] { "OperationId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowBindings_VersionId_Culture",
                table: "WhatsAppFlowBindings",
                columns: new[] { "VersionId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowOperations_NeutralOperationId",
                table: "WhatsAppFlowOperations",
                column: "NeutralOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowOperations_NextCheckAtUtc_Id",
                table: "WhatsAppFlowOperations",
                columns: new[] { "NextCheckAtUtc", "Id" },
                filter: "\"Completed\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowOperations_VersionId",
                table: "WhatsAppFlowOperations",
                column: "VersionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppFlowBindings");

            migrationBuilder.DropTable(
                name: "WhatsAppFlowOperations");
        }
    }
}
