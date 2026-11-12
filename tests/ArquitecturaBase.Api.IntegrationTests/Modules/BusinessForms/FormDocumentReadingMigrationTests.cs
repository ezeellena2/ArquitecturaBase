using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// AddFormDocumentReading (DOC1): the draft and the version keep the reading of documents in a nullable jsonb column each. The forms
// that were already there survive Up/Down/Up without one, and Down refuses a draft or a version that reads documents before touching
// the tables, instead of dropping what was written.
[Collection(ApiTestGroup.Name)]
public sealed class FormDocumentReadingMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddFormDocumentReading";
    private const string Reading = """{"enabled":true,"recognition":"Comprobante de una transferencia.","lookups":[]}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_reading_columns_arrive_and_leave_without_touching_the_forms_already_there()
    {
        var connection = factory.NewDatabaseConnectionString("form_document_reading");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var before = Before(db);
        try
        {
            await migrator.MigrateAsync(before, Ct);
            var definitionId = await InsertAsync(db);
            var history = await HistoryAsync(db);
            Assert.Equal(0, await ColumnsAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(2, await ColumnsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            await db.Database.ExecuteSqlAsync($"""UPDATE "FormDefinitions" SET "DocumentReadingJson" = CAST({Reading} AS jsonb) WHERE "Id" = {definitionId}""", Ct);
            await db.Database.ExecuteSqlAsync($"""UPDATE "FormDefinitions" SET "DocumentReadingJson" = NULL WHERE "Id" = {definitionId}""", Ct);

            await migrator.MigrateAsync(before, Ct);
            Assert.Equal(0, await ColumnsAsync(db));
            Assert.Equal(history, await HistoryAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await HistoryAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("FormDefinitions")]
    [InlineData("BusinessFormVersions")]
    public async Task Down_refuses_a_form_that_reads_documents_before_changing_the_tables(string table)
    {
        var connection = factory.NewDatabaseConnectionString("form_document_reading_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(cancellationToken: Ct);
            var definitionId = await InsertAsync(db);
            if (table == "FormDefinitions")
                await db.Database.ExecuteSqlAsync($"""UPDATE "FormDefinitions" SET "DocumentReadingJson" = CAST({Reading} AS jsonb) WHERE "Id" = {definitionId}""", Ct);
            else
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormVersions" SET "DocumentReadingJson" = CAST({Reading} AS jsonb) WHERE "DefinitionId" = {definitionId}""", Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before(db), Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(2, await ColumnsAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    // A published form with nothing to answer, written with the columns every schema has.
    private async Task<Guid> InsertAsync(ApplicationDbContext db)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var prefix = "L" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        const string names = """{"es":"Lecturas"}""";
        const string submit = """{"es":"Enviar"}""";
        const string draft = """{"format":1,"fields":[]}""";
        const string schema = """{"format":1,"questions":[]}""";
        var fingerprint = new string('e', 64);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "IsDeleted", "AppOnly")
            VALUES ({definitionId}, 'Lecturas', CAST({names} AS jsonb), {prefix}, 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 0, TRUE, 0, {now}, FALSE, TRUE);
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures", "AppOnly")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                CAST({submit} AS jsonb), 'es', 0, {now}, ARRAY['es'], TRUE);
            """, Ct);
        return definitionId;
    }

    private static Task<string[]> HistoryAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT (to_jsonb(d) - 'DocumentReadingJson')::text AS "Value" FROM "FormDefinitions" d
        UNION ALL
        SELECT (to_jsonb(v) - 'DocumentReadingJson')::text FROM "BusinessFormVersions" v
        ORDER BY 1
        """).ToArrayAsync(Ct);

    private static async Task<int> ColumnsAsync(ApplicationDbContext db) => await db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
        WHERE table_name IN ('FormDefinitions', 'BusinessFormVersions') AND column_name = 'DocumentReadingJson'
        """).SingleAsync(Ct);
}
