using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// VC1-A (task 29): AddWhatsAppSpreadsheetLoads only adds the footer of the Flow of a load and «Por qué lo propuso» of the notice of the
// loads, in the outgoing messages and in the messages. The previous schema cannot keep them, so its Down refuses while one is kept; without
// them it goes back, and going up again keeps the other messages as they were.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSpreadsheetLoadsMigrationTests(ApiFactory factory)
{
    private const string Migration = "20261113074019_AddWhatsAppSpreadsheetLoads";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notice_with_its_rows_refuses_the_downgrade_and_without_it_it_goes_back_and_up_again()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_spreadsheet_loads_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var migrations = db.Database.GetMigrations().ToList();
            var previous = migrations[migrations.IndexOf(Migration) - 1];
            var waId = MetaWebhook.UniqueWaId();
            var contact = WhatsAppContact.Create(waId, MetaWebhook.UniqueBsuid(), "Persona ficticia", now);
            var notice = WhatsAppOutgoingMessage.CreateAssistantText(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "Recibí una planilla con 7 filas.", now, 1, waId, 1);
            Assert.True(notice.ExplainSpreadsheetRows("""{"recognized":[],"sent":[],"forTeam":[],"repeated":[]}"""));
            var text = WhatsAppOutgoingMessage.CreateAssistantText(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "Recibí tu planilla.", now, 1, waId, 1);
            var sent = WhatsAppMessage.Outbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "Recibí una planilla con 7 filas.", now);
            db.AddRange(contact, notice, text, sent);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var withRows = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct));
            Assert.Equal("P0001", withRows.SqlState);
            Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));
            // The later migrations went back before this one refused (each in its own transaction): only the columns it knows are read.
            Assert.NotNull(await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(item => item.Id == notice.Id)
                .Select(item => item.SpreadsheetRowsJson).SingleAsync(Ct));

            await db.Database.ExecuteSqlAsync($"""DELETE FROM "WhatsAppOutgoingMessages" WHERE "Id" = {notice.Id}""", Ct);
            // Each column it cannot keep refuses on its own: the footer of a load, and «Por qué lo propuso» of a notice that went out.
            const string footer = "Cobranzas · prellenado";
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppOutgoingMessages" SET "Footer" = {footer} WHERE "Id" = {text.Id}""", Ct);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppOutgoingMessages" SET "Footer" = NULL WHERE "Id" = {text.Id}""", Ct);
            const string why = """{"recognized":[],"sent":[],"forTeam":[],"repeated":[]}""";
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppMessages" SET "SpreadsheetRowsJson" = CAST({why} AS jsonb) WHERE "Id" = {sent.Id}""", Ct);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
            Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppMessages" SET "SpreadsheetRowsJson" = NULL WHERE "Id" = {sent.Id}""", Ct);
            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));

            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var back = await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(item => item.Id == text.Id, Ct);
            Assert.Equal((WhatsAppOutgoingMessageKind.Text, "Recibí tu planilla.", (string?)null, (string?)null), (back.Kind, back.Body, back.Footer, back.SpreadsheetRowsJson));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
