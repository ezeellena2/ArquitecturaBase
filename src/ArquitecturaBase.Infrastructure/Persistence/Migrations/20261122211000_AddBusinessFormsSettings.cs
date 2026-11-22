using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFormsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFormsSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxFileSizeMiB = table.Column<int>(type: "integer", nullable: false),
                    DailyRereadsPerAsker = table.Column<int>(type: "integer", nullable: false),
                    SimilarDocumentsToSuggest = table.Column<int>(type: "integer", nullable: false),
                    ProposedExpiryDays = table.Column<int>(type: "integer", nullable: false),
                    DiscardedRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFormsSettings", x => x.Id);
                    table.CheckConstraint("CK_BusinessFormsSettings_SingleRow", "\"Id\" = '00000000-0000-0000-0000-0000000000f1'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFormsSettings");
        }
    }
}
