using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Task 8 (decision 10): AddWhatsAppInboundDocuments lets the inbox keep a file that could not be opened without its copy (the key, the
// SHA-256 and the size become nullable). Its Down refuses such a document before any DDL instead of deleting it, and goes back once none
// is left.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentFileMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddWhatsAppInboundDocuments";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Down_refuses_a_document_kept_without_its_file_and_goes_back_once_there_is_none()
    {
        var connection = factory.NewDatabaseConnectionString("form_received_document_file");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            // Only up to this migration: a later one would otherwise have to come down first, and this test is about this Down.
            await migrator.MigrateAsync(This(db), Ct);
            Assert.Equal(["YES", "YES", "YES"], await NullableAsync(db));
            // Only the columns of this schema (a later migration adds others the current model maps, VC1-A): a rejected file without its copy.
            var document = FormReceivedDocument.ReceiveRejected(Guid.CreateVersion7(), "whatsapp", "wamid.migration", Guid.NewGuid(), now, "informe.doc",
                "application/msword", 2048, "legacy", now).Value;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "FormReceivedDocuments" ("Id", "ChannelId", "SourceId", "SenderUserId", "ReceivedAtUtc", "FileName", "MediaType", "Size", "State",
                    "ReasonCode", "SearchText", "NotSendable", "DeleteAfterUtc", "Revision", "CreatedAtUtc")
                VALUES ({document.Id}, {document.ChannelId}, {document.SourceId}, {document.SenderUserId}, {document.ReceivedAtUtc}, {document.FileName},
                    {document.MediaType}, {document.Size}, 'Discarded', {document.ReasonCode}, '', FALSE, {document.DeleteAfterUtc}, 1, {now})
                """, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before(db), Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(["YES", "YES", "YES"], await NullableAsync(db));
            Assert.Equal("informe.doc", await db.Database.SqlQuery<string>(
                $"""SELECT "FileName" AS "Value" FROM "FormReceivedDocuments" WHERE "Id" = {document.Id}""").SingleAsync(Ct));

            await db.Database.ExecuteSqlAsync($"""DELETE FROM "FormReceivedDocuments" WHERE "Id" = {document.Id}""", Ct);
            await migrator.MigrateAsync(Before(db), Ct);
            Assert.Equal(["NO", "NO", "NO"], await NullableAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    // This migration, with its timestamp.
    private static string This(ApplicationDbContext db) =>
        db.Database.GetMigrations().Single(migration => migration.EndsWith("_" + Migration, StringComparison.Ordinal));

    private static Task<string[]> NullableAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT is_nullable AS "Value" FROM information_schema.columns
        WHERE table_name = 'FormReceivedDocuments' AND column_name IN ('StorageKey', 'Sha256', 'Size')
        ORDER BY column_name
        """).ToArrayAsync(Ct);
}
