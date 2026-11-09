using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ActivityStartOn",
                table: "ArcaInvoicingSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrossIncomeNumber",
                table: "ArcaInvoicingSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArcaVouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Class = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VoucherType = table.Column<int>(type: "integer", nullable: false),
                    Cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: false),
                    Concept = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentNumber = table.Column<long>(type: "bigint", nullable: false),
                    RequestedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CancelsVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Number = table.Column<long>(type: "bigint", nullable: true),
                    DataHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Cae = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    CaeDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MessagesJson = table.Column<string>(type: "jsonb", nullable: true),
                    Failures = table.Column<int>(type: "integer", nullable: false),
                    WaitedForArca = table.Column<bool>(type: "boolean", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PdfStorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PdfSize = table.Column<long>(type: "bigint", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaVouchers_ArcaVouchers_CancelsVoucherId",
                        column: x => x.CancelsVoucherId,
                        principalTable: "ArcaVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArcaVoucherSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: false),
                    VoucherType = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    PendingVoucherId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaVoucherSequences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_CancelsVoucherId",
                table: "ArcaVouchers",
                column: "CancelsVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_Environment_Cuit_PointOfSale_VoucherType_Number",
                table: "ArcaVouchers",
                columns: new[] { "Environment", "Cuit", "PointOfSale", "VoucherType", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_RequestKey",
                table: "ArcaVouchers",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_State_NextAttemptAtUtc",
                table: "ArcaVouchers",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVoucherSequences_Environment_Cuit_PointOfSale_VoucherTy~",
                table: "ArcaVoucherSequences",
                columns: new[] { "Environment", "Cuit", "PointOfSale", "VoucherType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArcaVouchers");

            migrationBuilder.DropTable(
                name: "ArcaVoucherSequences");

            migrationBuilder.DropColumn(
                name: "ActivityStartOn",
                table: "ArcaInvoicingSettings");

            migrationBuilder.DropColumn(
                name: "GrossIncomeNumber",
                table: "ArcaInvoicingSettings");
        }
    }
}
