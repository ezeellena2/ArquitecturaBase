using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring: AddWhatsAppInboundSpreadsheets only adds the spreadsheet's columns and the index of the pending
// ones. Its Down gives the previous image the media it knew, with its caption, and drops them; going up again keeps that row as it was.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSpreadsheetMigrationTests(ApiFactory factory)
{
    private const string Previous = "20261110020215_AddAssistantTurnAttachmentHashAndSize";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_adds_the_spreadsheet_and_Down_gives_back_the_media_with_its_caption()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_spreadsheet_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("IX_WhatsAppMessages_PendingSpreadsheets", await IndexesAsync(db));
            var contact = WhatsAppContact.Create("5493413654813", "AR.1102953142229032", "Persona ficticia", now);
            var spreadsheet = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-spreadsheet", WhatsAppMessageKind.Spreadsheet, "Armame un catálogo", null, now);
            Assert.True(spreadsheet.SetSpreadsheetDescriptor("1234", "text/csv", new string('a', 64), "clientes.csv"));
            Assert.True(spreadsheet.StoreSpreadsheet($"whatsapp/documents/{spreadsheet.Id:N}.csv", 3, new string('b', 64), "{\"rowCount\":1}").IsSuccess);
            var text = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-text", WhatsAppMessageKind.Text, "Hola", null, now);
            db.AddRange(contact, spreadsheet, text);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);

            Assert.DoesNotContain("SpreadsheetState", await ColumnsAsync(db));
            Assert.DoesNotContain("IX_WhatsAppMessages_PendingSpreadsheets", await IndexesAsync(db));
            Assert.Equal(("Media", "Armame un catálogo"), await KindAndBodyAsync(db, spreadsheet.Id));
            Assert.Equal(("Text", "Hola"), await KindAndBodyAsync(db, text.Id));

            await db.Database.MigrateAsync(Ct);

            var restored = await db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row => row.Id == spreadsheet.Id, Ct);
            Assert.Equal((WhatsAppMessageKind.Media, "Armame un catálogo", (WhatsAppSpreadsheetState?)null, 0),
                (restored.Kind, restored.Body, restored.SpreadsheetState, restored.SpreadsheetFailureCount));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    private static Task<List<string>> ColumnsAsync(ApplicationDbContext db) => db.Database.SqlQuery<string>(
        $"""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'WhatsAppMessages'""").ToListAsync(Ct);

    private static Task<List<string>> IndexesAsync(ApplicationDbContext db) => db.Database.SqlQuery<string>(
        $"""SELECT indexname AS "Value" FROM pg_indexes WHERE tablename = 'WhatsAppMessages'""").ToListAsync(Ct);

    private static async Task<(string Kind, string? Body)> KindAndBodyAsync(ApplicationDbContext db, Guid id)
    {
        var kind = await db.Database.SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct);
        var body = await db.Database.SqlQuery<string?>($"""SELECT "Body" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct);
        return (kind, body);
    }
}
