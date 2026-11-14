using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// H16 (ADR 0030): the encrypted content of the chat goes back to plain text only empty, so a migration test that goes below the
/// encryption empties it first, as the user would before going back.
/// </summary>
internal static class WhatsAppChatContentMigration
{
    public static async Task EmptyAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppOutgoingMessages" SET "Body" = NULL, "QuickRepliesJson" = NULL, "AssistantReplyJson" = NULL, "ListButton" = NULL, "Footer" = NULL, "SpreadsheetRowsJson" = NULL, "DocumentName" = NULL""", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppFlowSessions" SET "FlowBody" = NULL, "LocationLatitude" = NULL, "LocationLongitude" = NULL""", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppAssistantRequests" SET "OutcomeJson" = NULL""", cancellationToken);
    }
}
