using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// W3 (task 22): AddWhatsAppSharedContent only adds columns: the address of a place, the first card of a contact and the resend mark in the
// history, the resend mark in the durable outgoing (false in the ones of before) and what a send took of what was shared (task 23). Its
// Down gives the previous image the message of another kind a contact was before W3, keeping its row, and drops the columns; it refuses
// before any DDL while a resend or what a send took exists. Going up again reclassifies nothing.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSharedContentMigrationTests(ApiFactory factory)
{
    private const string Migration = "_AddWhatsAppSharedContent";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_adds_the_shared_content_and_Down_gives_back_a_contact_as_a_message_of_another_kind_keeping_its_row()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_shared_content_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var contact = WhatsAppContact.Create("5493413654813", "AR.1102953142229032", "Persona ficticia", now);
            var card = Inbound(contact, "wamid.migration-contact", WhatsAppMessageKind.Contact, 1, now);
            card.RecordContact("Ana Gómez", ["+54 9 341 365-4813"]);
            card.MarkProcessed(now);
            var place = Inbound(contact, "wamid.migration-place", WhatsAppMessageKind.Location, 2, now);
            place.RecordLocation(-32.947102m, -60.638955m, "Bv. Oroño 1234, Rosario");
            var other = Inbound(contact, "wamid.migration-other", WhatsAppMessageKind.Other, 3, now);
            var text = Inbound(contact, "wamid.migration-text", WhatsAppMessageKind.Text, 4, now, "Hola");
            var outgoing = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "Respuesta del operador", now);
            db.AddRange(contact, card, place, other, text, outgoing);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            await db.GetService<IMigrator>().MigrateAsync(Previous(db), Ct);

            Assert.Empty((await ColumnsAsync(db, "WhatsAppMessages")).Intersect(["LocationAddress", "ContactName", "ContactPhonesJson", "IsResend"]));
            Assert.DoesNotContain("IsResend", await ColumnsAsync(db, "WhatsAppOutgoingMessages"));
            Assert.Empty((await ColumnsAsync(db, "WhatsAppFlowSessions")).Intersect(["SharedContentJson", "ProtectedSharedValues"]));
            Assert.Equal(("Other", (string?)null, 1L, "Business", true), await RowAsync(db, card.Id));
            Assert.Equal(("Location", (string?)null, 2L, "Business", false), await RowAsync(db, place.Id));
            Assert.Equal(("Other", (string?)null, 3L, "Business", false), await RowAsync(db, other.Id));
            Assert.Equal(("Text", "Hola", 4L, "Business", false), await RowAsync(db, text.Id));
            Assert.Equal(-32.947102m, await db.Database.SqlQuery<decimal>($"""SELECT "Latitude" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {place.Id}""").SingleAsync(Ct));

            await db.Database.MigrateAsync(Ct);

            var restored = await db.Set<WhatsAppMessage>().AsNoTracking().Where(row => row.ContactId == contact.Id).ToDictionaryAsync(row => row.Id, Ct);
            Assert.Equal((WhatsAppMessageKind.Other, (string?)null, (string?)null, false, 1L),
                (restored[card.Id].Kind, restored[card.Id].ContactName, restored[card.Id].ContactPhonesJson, restored[card.Id].IsResend, restored[card.Id].InboundSequence));
            Assert.Equal((WhatsAppMessageKind.Location, (decimal?)-32.947102m, (string?)null),
                (restored[place.Id].Kind, restored[place.Id].Latitude, restored[place.Id].LocationAddress));
            Assert.Equal((WhatsAppMessageKind.Other, WhatsAppMessageKind.Text, "Hola"), (restored[other.Id].Kind, restored[text.Id].Kind, restored[text.Id].Body));
            Assert.False((await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(row => row.Id == outgoing.Id, Ct)).IsResend);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    // The previous image would read a resend as a second Flow of its send, and has no place for what a send took: the Down keeps every
    // column and row and goes back only once they are gone.
    [Theory]
    [InlineData("""UPDATE "WhatsAppMessages" SET "IsResend" = TRUE""", "WhatsAppMessages", "IsResend")]
    [InlineData("""UPDATE "WhatsAppOutgoingMessages" SET "IsResend" = TRUE""", "WhatsAppOutgoingMessages", "IsResend")]
    [InlineData("""UPDATE "WhatsAppFlowSessions" SET "SharedContentJson" = '[]'::jsonb""", "WhatsAppFlowSessions", "SharedContentJson")]
    [InlineData("""UPDATE "WhatsAppFlowSessions" SET "ProtectedSharedValues" = decode('0102','hex')""", "WhatsAppFlowSessions", "ProtectedSharedValues")]
    public async Task Down_refuses_before_any_DDL_while_a_resend_or_what_a_send_took_exists(string mark, string table, string column)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_shared_content_refusal")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            // A send of the operator with its Flow, its reply and an outgoing: one row of each table the Down checks.
            await WhatsAppAssistantMigrationSeed.InsertLegacyAsync(db, now, Ct);
            Assert.True(await db.Database.ExecuteSqlRawAsync(mark, Ct) > 0);
            var previous = Previous(db);

            var refused = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct));

            Assert.Contains("Resent forms", refused.MessageText, StringComparison.Ordinal);
            Assert.Contains(column, await ColumnsAsync(db, table));
            Assert.Contains("ContactPhonesJson", await ColumnsAsync(db, "WhatsAppMessages"));
            Assert.Contains(db.Database.GetMigrations().Single(id => id.EndsWith(Migration, StringComparison.Ordinal)),
                await db.Database.GetAppliedMigrationsAsync(Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    private static string Previous(ApplicationDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToList();
        return migrations[migrations.FindIndex(id => id.EndsWith(Migration, StringComparison.Ordinal)) - 1];
    }

    private static WhatsAppMessage Inbound(WhatsAppContact contact, string waMessageId, WhatsAppMessageKind kind, long sequence, DateTime now,
        string? body = null)
    {
        var message = WhatsAppMessage.Inbound(contact.Id, waMessageId, kind, body, null, now);
        message.Classify(WhatsAppMessagePurpose.Business);
        message.SetInboundSequence(sequence);
        return message;
    }

    private static Task<List<string>> ColumnsAsync(ApplicationDbContext db, string table) => db.Database.SqlQuery<string>(
        $"""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = {table}""").ToListAsync(Ct);

    // Read by SQL: after the Down the model of this build no longer matches the table.
    private static async Task<(string Kind, string? Body, long Sequence, string Purpose, bool Processed)> RowAsync(ApplicationDbContext db, Guid id) =>
        (await db.Database.SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct),
            await db.Database.SqlQuery<string?>($"""SELECT "Body" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct),
            await db.Database.SqlQuery<long>($"""SELECT "InboundSequence" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct),
            await db.Database.SqlQuery<string>($"""SELECT "Purpose" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct),
            await db.Database.SqlQuery<bool>($"""SELECT "ProcessedAtUtc" IS NOT NULL AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {id}""").SingleAsync(Ct));
}
