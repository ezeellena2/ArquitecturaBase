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

// H16 (ADR 0030), part 2: EncryptWhatsAppOutgoingContent keeps what a send says encrypted (bytea): nothing of it reads in SQL and
// the app reads it as it was written. Going up empties what there was, as the retention does; going back refuses before any DDL
// while there is encrypted content, and without any gives back the columns of before.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppOutgoingContentEncryptionMigrationTests(ApiFactory factory)
{
    private const string Migration = "_EncryptWhatsAppOutgoingContent";
    private static readonly string[] Columns = ["Body", "QuickRepliesJson", "AssistantReplyJson", "ListButton", "Footer", "SpreadsheetRowsJson", "DocumentName"];
    private static readonly string[] Marks =
    [
        """UPDATE "WhatsAppOutgoingMessages" SET "Body" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "QuickRepliesJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "AssistantReplyJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "ListButton" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "Footer" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "SpreadsheetRowsJson" = '\x01'::bytea""",
        """UPDATE "WhatsAppOutgoingMessages" SET "DocumentName" = '\x01'::bytea""",
    ];
    private const string ClearAll = """UPDATE "WhatsAppOutgoingMessages" SET "Body" = NULL, "QuickRepliesJson" = NULL, "AssistantReplyJson" = NULL, "ListButton" = NULL, "Footer" = NULL, "SpreadsheetRowsJson" = NULL, "DocumentName" = NULL""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_a_send_says_is_kept_encrypted_and_going_back_refuses_while_there_is_any()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_outgoing_content_encryption")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.All(await TypesAsync(db, Columns), type => Assert.Equal("bytea", type));
            var contact = WhatsAppContact.Create("5493413654813", null, "Persona ficticia", now);
            var outgoing = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "Respuesta del operador", now);
            db.AddRange(contact, outgoing);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var stored = await db.Database.SqlQuery<byte[]>($"""SELECT "Body" AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {outgoing.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("Respuesta", Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
            Assert.Equal("Respuesta del operador", await db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
                .Where(item => item.Id == outgoing.Id).Select(item => item.Body).SingleAsync(Ct));

            // Going back refuses while any of the seven keeps encrypted content.
            var previous = Previous(db);
            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppOutgoingMessages" SET "Body" = NULL""", Ct);
            foreach (var mark in Marks)
            {
                await db.Database.ExecuteSqlRawAsync(mark, Ct);
                Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
                await db.Database.ExecuteSqlRawAsync(ClearAll, Ct);
            }
            Assert.All(await TypesAsync(db, Columns), type => Assert.Equal("bytea", type));

            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.Equal(["character varying(4096)", "jsonb", "jsonb", "character varying(20)", "character varying(60)", "jsonb", "character varying(255)"],
                await TypesAsync(db, Columns));

            // What the image of before wrote goes empty when going up again, as the retention does.
            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppOutgoingMessages" SET "Body" = 'Texto anterior'""", Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.True(await db.Database.SqlQuery<bool>(
                $"""SELECT "Body" IS NULL AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {outgoing.Id}""").SingleAsync(Ct));
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
                $"""SELECT format_type(a.atttypid, a.atttypmod) AS "Value" FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid WHERE c.relname = 'WhatsAppOutgoingMessages' AND a.attname = {column}""").SingleAsync(Ct));
        }

        return [.. types];
    }
}
