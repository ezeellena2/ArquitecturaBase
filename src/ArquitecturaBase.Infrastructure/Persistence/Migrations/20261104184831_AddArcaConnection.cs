using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArcaConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EnabledServices = table.Column<string[]>(type: "text[]", nullable: false),
                    LastTestPassed = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaConnections", x => x.Id);
                    table.CheckConstraint("CK_ArcaConnections_Scope", "\"Scope\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "ArcaCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Alias = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequestPem = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ProtectedPrivateKey = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CertificatePem = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    NotBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Thumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaCertificates_ArcaConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ArcaConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArcaInvoicingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaInvoicingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaInvoicingSettings_ArcaConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ArcaConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArcaRegistryCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VatCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FiscalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaRegistryCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaRegistryCompanies_ArcaConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ArcaConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArcaAccessTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProtectedCredentials = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BlockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaAccessTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaAccessTickets_ArcaCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ArcaCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArcaAccessTickets_CertificateId_ServiceCode",
                table: "ArcaAccessTickets",
                columns: new[] { "CertificateId", "ServiceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArcaCertificates_ConnectionId_Role",
                table: "ArcaCertificates",
                columns: new[] { "ConnectionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArcaConnections_Scope",
                table: "ArcaConnections",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArcaInvoicingSettings_ConnectionId",
                table: "ArcaInvoicingSettings",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArcaRegistryCompanies_ConnectionId",
                table: "ArcaRegistryCompanies",
                column: "ConnectionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArcaAccessTickets");

            migrationBuilder.DropTable(
                name: "ArcaInvoicingSettings");

            migrationBuilder.DropTable(
                name: "ArcaRegistryCompanies");

            migrationBuilder.DropTable(
                name: "ArcaCertificates");

            migrationBuilder.DropTable(
                name: "ArcaConnections");
        }
    }
}
