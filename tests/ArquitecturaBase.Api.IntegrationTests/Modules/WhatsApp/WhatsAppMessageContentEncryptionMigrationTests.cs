using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// H16 (ADR 0030), part 3: EncryptWhatsAppMessageContent keeps what a message says encrypted (bytea), also the point of a place:
// nothing of it reads in SQL and the app reads it as it was written. Going up empties what there was, as the retention does; going
// back refuses before any DDL while there is encrypted content, and without any gives back the columns of before.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppMessageContentEncryptionMigrationTests(ApiFactory factory)
{
    private const string Migration = "_EncryptWhatsAppMessageContent";
    private static readonly string[] Columns = ["Body", "QuickReplyTitlesJson", "AssistantReplyJson", "SpreadsheetRowsJson", "Latitude", "Longitude", "LocationAddress", "ContactName", "ContactPhonesJson", "SpreadsheetFileName", "SpreadsheetPreparedJson", "DocumentFileName", "DocumentReadingJson"];
    private static readonly string[] Marks =
    [
        """UPDATE "WhatsAppMessages" SET "Body" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "QuickReplyTitlesJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "AssistantReplyJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "SpreadsheetRowsJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "Latitude" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "Longitude" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "LocationAddress" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "ContactName" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "ContactPhonesJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "SpreadsheetFileName" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "SpreadsheetPreparedJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "DocumentFileName" = '\x01'::bytea""",
        """UPDATE "WhatsAppMessages" SET "DocumentReadingJson" = '\x01'::bytea""",
    ];
    private const string ClearAll = """UPDATE "WhatsAppMessages" SET "Body" = NULL, "QuickReplyTitlesJson" = NULL, "AssistantReplyJson" = NULL, "SpreadsheetRowsJson" = NULL, "Latitude" = NULL, "Longitude" = NULL, "LocationAddress" = NULL, "ContactName" = NULL, "ContactPhonesJson" = NULL, "SpreadsheetFileName" = NULL, "SpreadsheetPreparedJson" = NULL, "DocumentFileName" = NULL, "DocumentReadingJson" = NULL""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_a_message_says_is_kept_encrypted_and_going_back_refuses_while_there_is_any()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_message_content_encryption")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.All(await TypesAsync(db, Columns), type => Assert.Equal("bytea", type));
            var contact = WhatsAppContact.Create("5493413654813", null, "Persona ficticia", now);
            var text = WhatsAppMessage.Inbound(contact.Id, "wamid.encrypted-text", WhatsAppMessageKind.Text, "Hola, quiero un turno", null, now);
            text.SetInboundSequence(contact.RegisterInboundMessage());
            var place = WhatsAppMessage.Inbound(contact.Id, "wamid.encrypted-place", WhatsAppMessageKind.Location, null, null, now);
            place.RecordLocation(-32.947102m, -60.639317m, "Calle ficticia 123");
            place.SetInboundSequence(contact.RegisterInboundMessage());
            db.AddRange(contact, text, place);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var stored = await db.Database.SqlQuery<byte[]>($"""SELECT "Body" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {text.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("turno", Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
            var point = await db.Database.SqlQuery<byte[]>($"""SELECT "Latitude" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {place.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("32.9", Encoding.UTF8.GetString(point), StringComparison.Ordinal);
            Assert.Equal("Hola, quiero un turno", await db.Set<WhatsAppMessage>().AsNoTracking()
                .Where(item => item.Id == text.Id).Select(item => item.Body).SingleAsync(Ct));
            var read = await db.Set<WhatsAppMessage>().AsNoTracking().Where(item => item.Id == place.Id)
                .Select(item => new { item.Latitude, item.Longitude, item.LocationAddress }).SingleAsync(Ct);
            Assert.Equal(((decimal?)-32.947102m, (decimal?)-60.639317m, (string?)"Calle ficticia 123"), (read.Latitude, read.Longitude, read.LocationAddress));

            // Going back refuses while any of the thirteen keeps encrypted content.
            var previous = Previous(db);
            await db.Database.ExecuteSqlRawAsync(ClearAll, Ct);
            foreach (var mark in Marks)
            {
                await db.Database.ExecuteSqlRawAsync(mark, Ct);
                Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
                await db.Database.ExecuteSqlRawAsync(ClearAll, Ct);
            }
            Assert.All(await TypesAsync(db, Columns), type => Assert.Equal("bytea", type));

            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.Equal(["character varying(4096)", "jsonb", "jsonb", "jsonb", "numeric(9,6)", "numeric(9,6)", "character varying(256)", "character varying(256)",
                "jsonb", "character varying(255)", "character varying(16384)", "character varying(255)", "character varying(131072)"], await TypesAsync(db, Columns));

            // What the image of before wrote goes empty when going up again, as the retention does.
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppMessages" SET "Body" = 'Texto anterior' WHERE "Id" = {text.Id}""", Ct);
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppMessages" SET "Latitude" = -32.947102, "Longitude" = -60.639317 WHERE "Id" = {place.Id}""", Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"""SELECT count(*)::int AS "Value" FROM "WhatsAppMessages" WHERE "Body" IS NOT NULL OR "Latitude" IS NOT NULL OR "Longitude" IS NOT NULL""").SingleAsync(Ct));
            Assert.Equal(2, await db.Set<WhatsAppMessage>().CountAsync(item => item.ContactId == contact.Id, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    private static string Previous(ApplicationDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToList();
        return migrations[migrations.FindIndex(id => id.EndsWith(Migration, StringComparison.Ordinal)) - 1];
    }

    private static async Task<string[]> TypesAsync(ApplicationDbContext db, string[] columns)
    {
        var types = new List<string>();
        foreach (var column in columns)
        {
            types.Add(await db.Database.SqlQuery<string>(
                $"""SELECT format_type(a.atttypid, a.atttypmod) AS "Value" FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid WHERE c.relname = 'WhatsAppMessages' AND a.attname = {column}""").SingleAsync(Ct));
        }

        return [.. types];
    }
}
