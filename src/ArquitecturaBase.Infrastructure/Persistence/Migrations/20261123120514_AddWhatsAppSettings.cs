using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowSessionLifetimeHours = table.Column<int>(type: "integer", nullable: false),
                    AllowedCountries = table.Column<string>(type: "jsonb", nullable: false),
                    MessageRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppSettings", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppSettings_FlowSessionLifetimeHours", "\"FlowSessionLifetimeHours\" BETWEEN 1 AND 168");
                    table.CheckConstraint("CK_WhatsAppSettings_MessageRetentionDays", "\"MessageRetentionDays\" BETWEEN 1 AND 3650");
                    table.CheckConstraint("CK_WhatsAppSettings_Singleton", "\"Id\" = '00000000-0000-0000-0000-00000000a9a1'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppSettings");
        }
    }
}
