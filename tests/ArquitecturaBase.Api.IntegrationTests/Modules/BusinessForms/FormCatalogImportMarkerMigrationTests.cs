using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogImportMarkerMigrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Import_marker_migrates_null_for_existing_catalogs_enforces_the_pair_and_preserves_historical_columns()
    {
        const string before = "20261022032434_AddCatalogValueType";
        var ct = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb",
            factory.NewDatabaseConnectionString("catalog_import_marker")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(before, ct);
            var id = Guid.CreateVersion7(); var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
            const string texts = "{\"es\":\"Histórico\",\"en\":\"Historical\"}";
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "IsActive", "Revision", "CreatedAtUtc")
                VALUES ({id}, 'IMPORT-MARKER', 'Manual', CAST({texts} AS jsonb), CAST({texts} AS jsonb), TRUE, 1, {nowUtc})
                """, ct);
            var original = await HistoricalCatalogAsync(db, ct);
            await migrator.MigrateAsync(cancellationToken: ct);
            Assert.True(await db.Database.SqlQueryRaw<bool>("""
                SELECT "LastImportSha256" IS NULL AND "LastImportAtUtc" IS NULL AS "Value" FROM "BusinessFormCatalogs"
                """).SingleAsync(ct));
            Assert.Equal(original, await HistoricalCatalogAsync(db, ct));
            var hash = new string('a', 64);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormCatalogs" SET "LastImportSha256" = {hash}, "LastImportAtUtc" = {nowUtc} WHERE "Id" = {id}
                """, ct);
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormCatalogs" SET "LastImportAtUtc" = NULL WHERE "Id" = {id}
                """, ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
            Assert.Equal("CK_BusinessFormCatalogs_LastImport", invalid.ConstraintName);
            await migrator.MigrateAsync(before, ct);
            Assert.Equal(original, await HistoricalCatalogAsync(db, ct));
            await migrator.MigrateAsync(cancellationToken: ct);
            Assert.Equal(original, await HistoricalCatalogAsync(db, ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(ct); }
    }

    private static Task<string> HistoricalCatalogAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('id', "Id", 'key', "Key", 'kind', "Kind", 'name', "NameJson", 'description', "DescriptionJson",
            'active', "IsActive", 'revision', "Revision", 'createdAtUtc', "CreatedAtUtc", 'valueType', "ValueType")::text AS "Value"
        FROM "BusinessFormCatalogs"
        """).SingleAsync(ct);
}
