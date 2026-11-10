using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// IA, Stage 2: AddWhatsAppAssistantAttachmentMessage only adds the spreadsheet message a reference carries, with its CHECK: what follows
// a captured response carries none. Down drops it and keeps the reference; going up again leaves it without one.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantAttachmentMigrationTests(ApiFactory factory)
{
    private const string Previous = "20261110051027_AddWhatsAppInboundSpreadsheets";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_adds_the_spreadsheet_a_reference_carries_and_Down_keeps_the_reference()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_assistant_attachment_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var contact = WhatsAppContact.Create("5493413654813", "AR.1102953142229033", "Persona ficticia", now);
            var spreadsheet = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-attachment", WhatsAppMessageKind.Spreadsheet, "Armame un catálogo", null, now);
            var reference = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, spreadsheet.Id, Guid.NewGuid(), Guid.NewGuid(),
                WhatsAppAssistantRequestKind.Automatic, 1, 1, "{\"channelId\":\"whatsapp-assistant\"}", now, now.AddDays(1));
            Assert.True(reference.CarryAttachment(spreadsheet.Id));
            db.AddRange(contact, spreadsheet, reference);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var violation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "WhatsAppAssistantRequests" SET "SourceRecordId" = {Guid.NewGuid()} WHERE "Id" = {reference.Id}""", Ct));
            Assert.Equal("CK_WhatsAppAssistantRequests_Attachment", violation.ConstraintName);

            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);

            Assert.DoesNotContain("AttachmentMessageId", await ColumnsAsync(db));
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "WhatsAppAssistantRequests" WHERE "Id" = {reference.Id}""").SingleAsync(Ct));

            await db.Database.MigrateAsync(Ct);

            var restored = await db.Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row => row.Id == reference.Id, Ct);
            Assert.Equal((spreadsheet.Id, (Guid?)null), (restored.SourceMessageId, restored.AttachmentMessageId));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    private static Task<List<string>> ColumnsAsync(ApplicationDbContext db) => db.Database.SqlQuery<string>(
        $"""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'WhatsAppAssistantRequests'""").ToListAsync(Ct);
}
