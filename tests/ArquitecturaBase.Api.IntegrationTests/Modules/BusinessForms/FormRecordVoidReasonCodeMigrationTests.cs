using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// AddFormRecordVoidReasonCode: a void the system decides («No se emitió») keeps the code of its reason, and a void keeps a text or a
// code, one of the two. The voids that were already there survive Up/Down/Up, and Down refuses a void with only a code before
// touching the table.
[Collection(ApiTestGroup.Name)]
public sealed class FormRecordVoidReasonCodeMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddFormRecordVoidReasonCode";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_reason_code_arrives_and_leaves_without_touching_the_voids_already_there()
    {
        var connection = factory.NewDatabaseConnectionString("void_reason_code");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var before = Before(db);
        try
        {
            await migrator.MigrateAsync(before, Ct);
            await InsertAsync(db, "Se cargó mal", null);
            var history = await HistoryAsync(db);
            Assert.False(await ColumnExistsAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.True(await ColumnExistsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            await InsertAsync(db, null, "Forms.Action.NotIssued");
            var both = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(db, "Texto", "Forms.Action.NotIssued"));
            Assert.Equal((PostgresErrorCodes.CheckViolation, "CK_BusinessFormRecords_Void"), (both.SqlState, both.ConstraintName));
            await db.Database.ExecuteSqlAsync($"""DELETE FROM "BusinessFormRecords" WHERE "VoidReasonCode" IS NOT NULL""", Ct);

            await migrator.MigrateAsync(before, Ct);
            Assert.False(await ColumnExistsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await HistoryAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Down_refuses_a_void_with_only_the_code_of_its_reason_before_changing_the_table()
    {
        var connection = factory.NewDatabaseConnectionString("void_reason_code_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(cancellationToken: Ct);
            await InsertAsync(db, null, "Forms.Action.NotIssued");
            // The migrations after this one go down first and succeed: the refusal is measured from this one.
            await migrator.MigrateAsync(This(db), Ct);
            var history = await HistoryAsync(db);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before(db), Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(history, await HistoryAsync(db));
            Assert.True(await ColumnExistsAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private static string This(ApplicationDbContext db) =>
        db.Database.GetMigrations().Single(migration => migration.EndsWith("_" + Migration, StringComparison.Ordinal));

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    // A voided response of a form with nothing to answer: with its reason as a text, as the code of a reason the system decided, or
    // with both, which the schema refuses.
    private async Task InsertAsync(ApplicationDbContext db, string? reason, string? code)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var recordId = Guid.CreateVersion7();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var prefix = "V" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        const string names = """{"es":"Anuladas"}""";
        const string submit = """{"es":"Enviar"}""";
        const string draft = """{"format":1,"fields":[]}""";
        const string schema = """{"format":1,"questions":[]}""";
        var fingerprint = new string('d', 64);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "IsDeleted", "AppOnly")
            VALUES ({definitionId}, 'Anuladas', CAST({names} AS jsonb), {prefix}, 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 0, TRUE, 1, {now}, FALSE, FALSE);
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                CAST({submit} AS jsonb), 'es', 0, {now}, ARRAY['es']);
            """, Ct);
        if (code is null)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                    "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State", "EnteredInApp", "VoidedByUserId", "VoidedAtUtc", "VoidReason")
                VALUES ({recordId}, {definitionId}, {versionId}, 1, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {now}, CAST('[]' AS jsonb), {now},
                    'Voided', FALSE, {Guid.CreateVersion7()}, {now}, {reason});
                """, Ct);
        else
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                    "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State", "EnteredInApp", "VoidedByUserId", "VoidedAtUtc", "VoidReason", "VoidReasonCode")
                VALUES ({recordId}, {definitionId}, {versionId}, 1, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {now}, CAST('[]' AS jsonb), {now},
                    'Voided', FALSE, {Guid.CreateVersion7()}, {now}, {reason}, {code});
                """, Ct);
    }

    private static Task<string[]> HistoryAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT (to_jsonb(r) - 'VoidReasonCode')::text AS "Value" FROM "BusinessFormRecords" r ORDER BY "Value"
        """).ToArrayAsync(Ct);

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext db) => await db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'BusinessFormRecords' AND column_name = 'VoidReasonCode'
        """).SingleAsync(Ct) == 1;
}
