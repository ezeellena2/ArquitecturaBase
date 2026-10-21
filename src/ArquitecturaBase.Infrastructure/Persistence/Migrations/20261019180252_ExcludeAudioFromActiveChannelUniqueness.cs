using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExcludeAudioFromActiveChannelUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running')\nAND (CASE WHEN \"ExecutionProfileJson\" IS JSON OBJECT THEN \"ExecutionProfileJson\"::jsonb -> 'purpose' END) IS DISTINCT FROM '1'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring the previous index must not discard or terminate concurrent accepted work.
            migrationBuilder.Sql("""
                LOCK TABLE "AiAssistantTurns" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT "OwnerUserId", "ChannelId" FROM "AiAssistantTurns"
                        WHERE "ChannelId" <> 'home' AND "State" IN ('Pending', 'Running')
                        GROUP BY "OwnerUserId", "ChannelId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Active assistant operations must finish before restoring actor-channel uniqueness.';
                    END IF;
                END;
                $$;
                """.ReplaceLineEndings("\n"));

            migrationBuilder.DropIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantTurns_OwnerUserId_ChannelId",
                table: "AiAssistantTurns",
                columns: new[] { "OwnerUserId", "ChannelId" },
                unique: true,
                filter: "\"ChannelId\" <> 'home' AND \"State\" IN ('Pending', 'Running')");
        }
    }
}
