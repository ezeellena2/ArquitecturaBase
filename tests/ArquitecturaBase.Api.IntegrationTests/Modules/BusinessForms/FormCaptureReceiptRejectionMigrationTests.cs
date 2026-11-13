using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Review of task 9c of the documents: AddCaptureReceiptRejectionDetails adds what a rejected capture said (a fixed validation, or the
// number and the reception of the response it repeats), so its replay says it again. A receipt that was already there keeps everything
// and says neither, as its replay did before, across Up/Down/Up.
[Collection(ApiTestGroup.Name)]
public sealed class FormCaptureReceiptRejectionMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddCaptureReceiptRejectionDetails";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_a_rejection_said_arrives_and_leaves_without_touching_the_receipts_already_there()
    {
        var connection = factory.NewDatabaseConnectionString("receipt_rejection");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var before = Before(db);
        try
        {
            await migrator.MigrateAsync(before, Ct);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint", "State", "ErrorCode",
                    "CreatedAtUtc")
                VALUES ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {new string('d', 64)},
                    'Rejected', 'Forms.Record.BankAccountInvalid', {factory.Clock.GetUtcNow().UtcDateTime});
                """, Ct);
            var history = await HistoryAsync(db);
            Assert.Equal(0, await ColumnsAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(3, await ColumnsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal("false||", await db.Database.SqlQueryRaw<string>("""
                SELECT concat("FixedValidation"::text, '|', "RepeatedRecordNumber", '|', "RepeatedAtUtc"::text) AS "Value" FROM "BusinessFormCaptureReceipts"
                """).SingleAsync(Ct));

            await migrator.MigrateAsync(before, Ct);
            Assert.Equal(0, await ColumnsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await HistoryAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    private static Task<string[]> HistoryAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT (to_jsonb(r) - 'FixedValidation' - 'RepeatedRecordNumber' - 'RepeatedAtUtc')::text AS "Value" FROM "BusinessFormCaptureReceipts" r
        ORDER BY "Value"
        """).ToArrayAsync(Ct);

    private static Task<int> ColumnsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'BusinessFormCaptureReceipts'
            AND column_name IN ('FixedValidation', 'RepeatedRecordNumber', 'RepeatedAtUtc')
        """).SingleAsync(Ct);
}
