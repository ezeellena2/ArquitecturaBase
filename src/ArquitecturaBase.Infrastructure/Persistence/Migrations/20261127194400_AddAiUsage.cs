using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    InputUsdPerMillion = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    OutputUsdPerMillion = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AudioUsdPerMinute = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageMonths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Requests = table.Column<long>(type: "bigint", nullable: false),
                    UnknownUsageRequests = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    AudioSeconds = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageMonths", x => x.Id);
                    table.CheckConstraint("CK_AiUsageMonths_Amounts", "\"Requests\" >= 0 AND \"UnknownUsageRequests\" >= 0 AND \"UnknownUsageRequests\" <= \"Requests\"");
                    table.CheckConstraint("CK_AiUsageMonths_FirstDay", "EXTRACT(DAY FROM \"Month\") = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelPrices_Provider_Model",
                table: "AiModelPrices",
                columns: new[] { "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageMonths_Month_Provider_Model",
                table: "AiUsageMonths",
                columns: new[] { "Month", "Provider", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiModelPrices");

            migrationBuilder.DropTable(
                name: "AiUsageMonths");
        }
    }
}
