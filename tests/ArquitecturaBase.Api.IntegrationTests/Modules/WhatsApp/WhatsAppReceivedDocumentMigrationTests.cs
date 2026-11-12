using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 8 of the documents: AddWhatsAppInboundDocuments only adds the received document's columns, the unique index of its reading's request
// and the index of the ones that wait. Its Down gives the previous image the media it knew, with its caption, as the spreadsheets' Down
// does, and drops them; going up again keeps that row as it was.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppReceivedDocumentMigrationTests(ApiFactory factory)
{
    private const string Previous = "20261112122715_AddFormReceivedDocuments";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_adds_the_received_document_and_Down_gives_back_the_media_with_its_caption()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_document_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("IX_WhatsAppMessages_PendingDocuments", await IndexesAsync(db));
            Assert.Contains("IX_WhatsAppMessages_DocumentRequestId", await IndexesAsync(db));
            var contact = WhatsAppContact.Create("5493413654813", "AR.1102953142229032", "Persona ficticia", now);
            var document = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-document", WhatsAppMessageKind.ReceivedDocument, "Cargalo en pagos", null, now);
            Assert.True(document.SetDocumentDescriptor("1234", "application/pdf", new string('a', 64), "comprobante.pdf"));
            Assert.True(document.StoreDocument($"whatsapp/documents/{document.Id:N}.pdf", 3, new string('b', 64), 1, null).IsSuccess);
            Assert.True(document.RequestDocumentReading(Guid.NewGuid(), "{\"channelId\":\"whatsapp-assistant\"}", 1, now.AddDays(90), now).IsSuccess);
            var spreadsheet = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-spreadsheet", WhatsAppMessageKind.Spreadsheet, "Armame un catálogo", null, now);
            Assert.True(spreadsheet.SetSpreadsheetDescriptor("1235", "text/csv", new string('c', 64), "clientes.csv"));
            var text = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-text", WhatsAppMessageKind.Text, "Hola", null, now);
            db.AddRange(contact, document, spreadsheet, text);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);

            Assert.DoesNotContain("DocumentState", await ColumnsAsync(db));
            Assert.DoesNotContain("DocumentReadingJson", await ColumnsAsync(db));
            Assert.DoesNotContain("IX_WhatsAppMessages_PendingDocuments", await IndexesAsync(db));
            Assert.Equal(("Media", "Cargalo en pagos"), await KindAndBodyAsync(db, document.Id));
            Assert.Equal(("Spreadsheet", "Armame un catálogo"), await KindAndBodyAsync(db, spreadsheet.Id));
            Assert.Equal(("Text", "Hola"), await KindAndBodyAsync(db, text.Id));

            await db.Database.MigrateAsync(Ct);

            var restored = await db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row => row.Id == document.Id, Ct);
            Assert.Equal((WhatsAppMessageKind.Media, "Cargalo en pagos", (WhatsAppDocumentState?)null, 0, (Guid?)null),
                (restored.Kind, restored.Body, restored.DocumentState, restored.DocumentFailureCount, restored.DocumentRequestId));
            Assert.Equal(WhatsAppSpreadsheetState.Pending,
                (await db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row => row.Id == spreadsheet.Id, Ct)).SpreadsheetState);
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
