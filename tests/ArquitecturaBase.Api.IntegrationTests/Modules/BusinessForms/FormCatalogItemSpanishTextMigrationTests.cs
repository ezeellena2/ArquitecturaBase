using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogItemSpanishTextMigrationTests(ApiFactory factory)
{
    private const string Before = "20261023224251_AddCatalogAndPersonAttributeOriginalCulture";
    private const string WithoutSpanishText = "AllowCatalogItemsWithoutSpanishText";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Spanish_text_becomes_optional_and_existing_options_survive_up_down_up()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_item_spanish_text");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            await InsertAsync(db, "es");
            var history = await HistoryAsync(db);
            Assert.Equal("NO:", await ColumnAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal("YES:", await ColumnAsync(db));

            await migrator.MigrateAsync(Before, Ct);
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal("NO:", await ColumnAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await HistoryAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Down_refuses_an_option_without_Spanish_text_before_changing_the_column()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_item_spanish_text_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            var itemId = await InsertAsync(db, "en");
            await migrator.MigrateAsync(WithoutSpanishText, Ct);
            await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogItems" SET "TextEs" = NULL WHERE "Id" = {itemId}""", Ct);
            var history = await HistoryAsync(db);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before, Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal("YES:", await ColumnAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private async Task<Guid> InsertAsync(ApplicationDbContext db, string originalCulture)
    {
        var catalogId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        const string name = "{\"es\":\"Sucursales\",\"en\":\"Branches\"}";
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "OriginalCulture", "IsActive", "Revision", "CreatedAtUtc")
            VALUES ({catalogId}, 'SPANISH-TEXT', 'Manual', CAST({name} AS jsonb), CAST({name} AS jsonb), {originalCulture}, TRUE, 1, {now});
            INSERT INTO "BusinessFormCatalogItems" ("Id", "CatalogId", "Code", "TextEs", "TextEn", "DetailEs", "DetailEn", "Position", "IsActive", "CreatedAtUtc")
            VALUES ({itemId}, {catalogId}, 'NORTH', 'Norte', 'North', 'Zona norte', 'Northern area', 1, TRUE, {now});
            """, Ct);
        return itemId;
    }

    private static Task<string[]> HistoryAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'catalog', 'data', to_jsonb(r))::text AS "Value" FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', to_jsonb(r))::text FROM "BusinessFormCatalogItems" r
        ORDER BY "Value"
        """).ToArrayAsync(Ct);

    // Whether TextEs admits NULL and its default (empty when it has none).
    private static Task<string> ColumnAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT is_nullable || ':' || COALESCE(column_default, '') AS "Value" FROM information_schema.columns
        WHERE table_name = 'BusinessFormCatalogItems' AND column_name = 'TextEs'
        """).SingleAsync(Ct);
}
