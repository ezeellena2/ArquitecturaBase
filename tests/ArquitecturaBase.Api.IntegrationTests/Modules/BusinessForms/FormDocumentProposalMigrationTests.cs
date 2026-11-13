using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// AddFormDocumentProposals (DOC6): the requests of «Armar formulario con la IA» get a table of their own. Empty, it arrives and leaves with
// Up/Down/Up; with a request in it, Down refuses before touching the table, instead of dropping what was asked.
[Collection(ApiTestGroup.Name)]
public sealed class FormDocumentProposalMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddFormDocumentProposals";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_table_arrives_and_leaves_while_empty_and_Down_refuses_a_request_before_dropping_it()
    {
        var connection = factory.NewDatabaseConnectionString("form_document_proposals");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var before = Before(db);
        try
        {
            await migrator.MigrateAsync(before, Ct);
            Assert.Equal(0, await TablesAsync(db));
            // Only up to this migration: a later one would otherwise have to come down first, and this test is about this Down.
            await migrator.MigrateAsync(This(db), Ct);
            Assert.Equal(1, await TablesAsync(db));
            await migrator.MigrateAsync(before, Ct);
            Assert.Equal(0, await TablesAsync(db));
            await migrator.MigrateAsync(This(db), Ct);

            var now = factory.Clock.GetUtcNow().UtcDateTime;
            Guid[] documents = [Guid.CreateVersion7(), Guid.CreateVersion7()];
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "FormDocumentProposals" ("Id", "RequestId", "ActorUserId", "Culture", "TimeZoneId", "RequestedAtUtc", "DocumentIds",
                    "PendingRereadIds", "RereadsOnDay", "CreatedAtUtc")
                VALUES ({Guid.CreateVersion7()}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'es', 'UTC', {now}, {documents}, {Array.Empty<Guid>()}, 0, {now})
                """, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(before, Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "FormDocumentProposals" """).SingleAsync(Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    // This migration, with its timestamp.
    private static string This(ApplicationDbContext db) =>
        db.Database.GetMigrations().Single(migration => migration.EndsWith("_" + Migration, StringComparison.Ordinal));

    private static async Task<int> TablesAsync(ApplicationDbContext db) => await db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.tables WHERE table_name = 'FormDocumentProposals'
        """).SingleAsync(Ct);
}
