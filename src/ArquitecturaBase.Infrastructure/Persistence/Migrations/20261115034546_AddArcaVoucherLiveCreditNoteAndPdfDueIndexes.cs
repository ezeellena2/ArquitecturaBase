using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaVoucherLiveCreditNoteAndPdfDueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_CancelsVoucherId_Live",
                table: "ArcaVouchers",
                column: "CancelsVoucherId",
                unique: true,
                filter: "\"CancelsVoucherId\" IS NOT NULL AND \"State\" NOT IN ('Rejected', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_ArcaVouchers_PdfDue",
                table: "ArcaVouchers",
                column: "NextAttemptAtUtc",
                filter: "\"State\" = 'Authorized' AND \"PdfStorageKey\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArcaVouchers_CancelsVoucherId_Live",
                table: "ArcaVouchers");

            migrationBuilder.DropIndex(
                name: "IX_ArcaVouchers_PdfDue",
                table: "ArcaVouchers");
        }
    }
}
