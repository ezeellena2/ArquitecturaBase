using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormReceivedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormReceivedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", maxLength: 64, nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: true),
                    ProtectedReading = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedProposal = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    SearchText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplyExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotSendable = table.Column<bool>(type: "boolean", nullable: false),
                    DeleteAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtractionRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtractionActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionCulture = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ExtractionTimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormReceivedDocuments", x => x.Id);
                    table.CheckConstraint("CK_FormReceivedDocuments_Confirmed", "(\"State\" = 'Confirmed') = (\"RecordId\" IS NOT NULL)");
                    table.CheckConstraint("CK_FormReceivedDocuments_Deletion", "(\"State\" IN ('Discarded', 'Unclassified')) = (\"DeleteAfterUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_FormReceivedDocuments_Extraction", "(\"ExtractionRequestId\" IS NULL) = (\"ExtractionDefinitionId\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionRequestedAtUtc\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionActorUserId\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionCulture\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionTimeZoneId\" IS NULL)");
                    table.CheckConstraint("CK_FormReceivedDocuments_Proposed", "\"State\" <> 'Proposed' OR (\"DefinitionId\" IS NOT NULL AND \"VersionId\" IS NOT NULL AND \"ProtectedProposal\" IS NOT NULL)");
                    table.CheckConstraint("CK_FormReceivedDocuments_Review", "(\"ReviewedByUserId\" IS NULL) = (\"ReviewedAtUtc\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_FormReceivedDocuments_BusinessFormRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "BusinessFormRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormReceivedDocuments_BusinessFormVersions_DefinitionId_Ver~",
                        columns: x => new { x.DefinitionId, x.VersionId },
                        principalTable: "BusinessFormVersions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormReceivedDocuments_FormDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormReceivedDocumentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedDetail = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormReceivedDocumentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormReceivedDocumentEvents_FormReceivedDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "FormReceivedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocumentEvents_DocumentId_AtUtc",
                table: "FormReceivedDocumentEvents",
                columns: new[] { "DocumentId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_ChannelId_SourceId",
                table: "FormReceivedDocuments",
                columns: new[] { "ChannelId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_DefinitionId_Sha256",
                table: "FormReceivedDocuments",
                columns: new[] { "DefinitionId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_DefinitionId_VersionId",
                table: "FormReceivedDocuments",
                columns: new[] { "DefinitionId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_DeleteAfterUtc",
                table: "FormReceivedDocuments",
                column: "DeleteAfterUtc",
                filter: "\"DeleteAfterUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_ReceivedAtUtc",
                table: "FormReceivedDocuments",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_RecordId",
                table: "FormReceivedDocuments",
                column: "RecordId",
                unique: true,
                filter: "\"RecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_SearchText",
                table: "FormReceivedDocuments",
                column: "SearchText")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "simple");

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_State_ReceivedAtUtc",
                table: "FormReceivedDocuments",
                columns: new[] { "State", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormReceivedDocuments_StorageKey",
                table: "FormReceivedDocuments",
                column: "StorageKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormReceivedDocumentEvents");

            migrationBuilder.DropTable(
                name: "FormReceivedDocuments");
        }
    }
}
