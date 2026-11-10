using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArcaRobotRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArcaRobotRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedSteps = table.Column<string[]>(type: "text[]", nullable: false),
                    CurrentStep = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Outputs = table.Column<string>(type: "jsonb", nullable: false),
                    Failure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    FailedStep = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsentedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AlertKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AlertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArcaRobotRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArcaRobotRuns_ArcaConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ArcaConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArcaRobotRuns_ConnectionId_Side",
                table: "ArcaRobotRuns",
                columns: new[] { "ConnectionId", "Side" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArcaRobotRuns");
        }
    }
}
