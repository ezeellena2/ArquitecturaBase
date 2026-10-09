using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiBusinessInformation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiBusinessInformation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Hours = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Services = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Policies = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Faq = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBusinessInformation", x => x.Id);
                    table.CheckConstraint("CK_AiBusinessInformation_Scope", "\"Scope\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "AiBusinessInformationChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBusinessInformationChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiBusinessInformation_Scope",
                table: "AiBusinessInformation",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiBusinessInformationChanges_CreatedAtUtc",
                table: "AiBusinessInformationChanges",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiBusinessInformationChanges_OperatorId_RequestId",
                table: "AiBusinessInformationChanges",
                columns: new[] { "OperatorId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiBusinessInformation");

            migrationBuilder.DropTable(
                name: "AiBusinessInformationChanges");
        }
    }
}
