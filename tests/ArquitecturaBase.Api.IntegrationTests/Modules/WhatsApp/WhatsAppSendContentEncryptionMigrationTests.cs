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

// H16 (ADR 0030), part 1: EncryptWhatsAppSendContent keeps the body and the point of a send, and why a reference ended without a
// message, encrypted (bytea): nothing of it reads in SQL and the app reads it as it was written. Going up empties what there was,
// as the retention does; going back refuses before any DDL while there is encrypted content, and without any gives back the columns
// of before.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSendContentEncryptionMigrationTests(ApiFactory factory)
{
    private const string Migration = "_EncryptWhatsAppSendContent";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_content_of_a_send_is_kept_encrypted_and_going_back_refuses_while_there_is_any()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_send_content_encryption")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(["bytea", "bytea", "bytea"], await TypesAsync(db, "WhatsAppFlowSessions", "FlowBody", "LocationLatitude", "LocationLongitude"));
            Assert.Equal(["bytea"], await TypesAsync(db, "WhatsAppAssistantRequests", "OutcomeJson"));
            var contact = WhatsAppContact.Create("5493413654813", null, "Persona ficticia", now);
            var operation = WhatsAppFlowOperation.Accept(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            operation.SealManifest([1]);
            var binding = WhatsAppFlowBinding.Create(operation.Id, operation.VersionId, "es", "Visita", [2]);
            var session = WhatsAppFlowSession.Create(contact.Id, 0, Guid.NewGuid(), Guid.NewGuid(), operation.DefinitionId, operation.VersionId, 1,
                binding.Id, "es", "{\"es\":\"Visita\"}", "VIS", new string('a', 64), new string('b', 64), now.AddHours(1));
            session.SealPayload([8]);
            session.RequestLocation(true, "Cuerpo del Flow");
            Assert.True(session.ReceiveLocation(-34.603722m, -58.381592m, "wamid.migration-point", now));
            var source = WhatsAppMessage.Inbound(contact.Id, "wamid.migration-source", WhatsAppMessageKind.Text, "Hola", null, now);
            source.SetInboundSequence(contact.RegisterInboundMessage());
            var reference = WhatsAppAssistantRequest.Create(Guid.NewGuid(), contact.Id, source.Id, Guid.NewGuid(), Guid.NewGuid(),
                WhatsAppAssistantRequestKind.Automatic, 0, 0, "{}", now, now.AddDays(1));
            Assert.True(reference.RecordOutcome("""{"why":"Sin mensaje"}"""));
            db.AddRange(contact, operation, binding, session, source, reference);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var stored = await db.Database.SqlQuery<byte[]>($"""SELECT "FlowBody" AS "Value" FROM "WhatsAppFlowSessions" WHERE "Id" = {session.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("Cuerpo", Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
            var point = await db.Database.SqlQuery<byte[]>($"""SELECT "LocationLatitude" AS "Value" FROM "WhatsAppFlowSessions" WHERE "Id" = {session.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("34.603722", Encoding.UTF8.GetString(point), StringComparison.Ordinal);
            var read = await db.Set<WhatsAppFlowSession>().AsNoTracking().SingleAsync(item => item.Id == session.Id, Ct);
            Assert.Equal(("Cuerpo del Flow", (decimal?)-34.603722m, (decimal?)-58.381592m), (read.FlowBody, read.LocationLatitude, read.LocationLongitude));
            var why = await db.Database.SqlQuery<byte[]>($"""SELECT "OutcomeJson" AS "Value" FROM "WhatsAppAssistantRequests" WHERE "Id" = {reference.Id}""").SingleAsync(Ct);
            Assert.DoesNotContain("Sin mensaje", Encoding.UTF8.GetString(why), StringComparison.Ordinal);
            Assert.Equal("""{"why":"Sin mensaje"}""", await db.Set<WhatsAppAssistantRequest>().AsNoTracking()
                .Where(item => item.Id == reference.Id).Select(item => item.OutcomeJson).SingleAsync(Ct));

            // Going back refuses while there is encrypted content, in either table. What the messages say (part 3) goes back first, and
            // empty, so each refusal below is this part's.
            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppMessages" SET "Body" = NULL""", Ct);
            var previous = Previous(db);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppFlowSessions" SET "FlowBody" = NULL, "LocationLatitude" = NULL, "LocationLongitude" = NULL""", Ct);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct))).SqlState);
            Assert.Equal(["bytea"], await TypesAsync(db, "WhatsAppAssistantRequests", "OutcomeJson"));

            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppAssistantRequests" SET "OutcomeJson" = NULL""", Ct);
            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.Equal(["character varying(1024)", "numeric(9,6)", "numeric(9,6)"],
                await TypesAsync(db, "WhatsAppFlowSessions", "FlowBody", "LocationLatitude", "LocationLongitude"));
            Assert.Equal(["jsonb"], await TypesAsync(db, "WhatsAppAssistantRequests", "OutcomeJson"));

            // What the image of before wrote goes empty when going up again, as the retention does.
            await db.Database.ExecuteSqlRawAsync("""UPDATE "WhatsAppFlowSessions" SET "FlowBody" = 'Cuerpo anterior', "LocationLatitude" = -34.6""", Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"""SELECT count(*)::int AS "Value" FROM "WhatsAppFlowSessions" WHERE "FlowBody" IS NOT NULL OR "LocationLatitude" IS NOT NULL""").SingleAsync(Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    private static string Previous(ApplicationDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToList();
        return migrations[migrations.FindIndex(id => id.EndsWith(Migration, StringComparison.Ordinal)) - 1];
    }

    private static async Task<string[]> TypesAsync(ApplicationDbContext db, string table, params string[] columns)
    {
        var types = new List<string>();
        foreach (var column in columns)
        {
            types.Add(await db.Database.SqlQuery<string>(
                $"""SELECT format_type(a.atttypid, a.atttypmod) AS "Value" FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid WHERE c.relname = {table} AND a.attname = {column}""").SingleAsync(Ct));
        }

        return [.. types];
    }
}
