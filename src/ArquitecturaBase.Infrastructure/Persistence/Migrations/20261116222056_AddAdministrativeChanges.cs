using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdministrativeChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Added = table.Column<string[]>(type: "text[]", nullable: false),
                    Removed = table.Column<string[]>(type: "text[]", nullable: false),
                    Before = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    After = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrativeChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeChanges_ActorUserId_OccurredAtUtc",
                table: "AdministrativeChanges",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeChanges_TargetId_OccurredAtUtc",
                table: "AdministrativeChanges",
                columns: new[] { "TargetId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministrativeChanges");
        }
    }
}
