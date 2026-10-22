using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogCodeUniquenessMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deferred_codes_allow_a_transactional_swap_preserve_rows_and_keep_inactive_codes_reserved()
    {
        const string previousMigration = "20261022000021_SealFormPublishedCultures";
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb",
            factory.NewDatabaseConnectionString("catalog_code_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(previousMigration, Ct);
            var catalogId = Guid.CreateVersion7();
            var firstId = Guid.CreateVersion7();
            var secondId = Guid.CreateVersion7();
            var inactiveId = Guid.CreateVersion7();
            var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
            const string names = """{"es":"Catálogo de prueba","en":"Test catalog"}""";
            // The previous schema must be seeded through its own columns, not the evolving EF model.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "IsActive", "Revision", "CreatedAtUtc")
                VALUES ({catalogId}, 'CODE-MIGRATION', 'Manual', CAST({names} AS jsonb), CAST({names} AS jsonb), TRUE, 1, {nowUtc});
                INSERT INTO "BusinessFormCatalogItems"
                    ("Id", "CatalogId", "Code", "TextEs", "TextEn", "DetailEs", "DetailEn", "Position", "IsActive", "CreatedAtUtc")
                VALUES
                    ({firstId}, {catalogId}, 'A', 'Primero', 'First', 'Detalle uno', 'Detail one', 1, TRUE, {nowUtc}),
                    ({secondId}, {catalogId}, 'B', 'Segundo', 'Second', NULL, NULL, 2, TRUE, {nowUtc}),
                    ({inactiveId}, {catalogId}, 'RESERVED', 'Inactivo', 'Inactive', NULL, NULL, 3, FALSE, {nowUtc});
                """, Ct);
            var original = await HistoricalRowsAsync(db);
            Assert.True(await ImmediateCodeIndexExistsAsync(db));
            Assert.False(await DeferredCodeConstraintExistsAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.True(await DeferredCodeConstraintExistsAsync(db));
            Assert.False(await ImmediateCodeIndexExistsAsync(db));
            Assert.True(await db.Database.SqlQueryRaw<bool>("""
                SELECT condeferrable AND condeferred AS "Value" FROM pg_constraint
                WHERE conname = 'AK_BusinessFormCatalogItems_CatalogId_Position'
                """).SingleAsync(Ct));
            Assert.Equal(original, await HistoricalRowsAsync(db));
            var catalog = await db.Set<BusinessFormCatalog>().SingleAsync(value => value.Id == catalogId, Ct);
            var items = await db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalogId).OrderBy(item => item.Position).ToArrayAsync(Ct);
            var first = items[0];
            var second = items[1];
            var inactive = items[2];

            Assert.True(catalog.UpdateManualItem(first, "B", first.TextEs, first.TextEn, first.DetailEs, first.DetailEn, first.Position).IsSuccess);
            Assert.True(catalog.UpdateManualItem(second, "A", second.TextEs, second.TextEn, second.DetailEs, second.DetailEn, second.Position).IsSuccess);
            await db.SaveChangesAsync(Ct);
            var swapped = await db.Set<BusinessFormCatalogItem>().AsNoTracking().OrderBy(item => item.Position).ToArrayAsync(Ct);
            Assert.Equal([first.Id, second.Id, inactive.Id], swapped.Select(item => item.Id));
            Assert.Equal(["B", "A", "RESERVED"], swapped.Select(item => item.Code));
            Assert.Equal([1, 2, 3], swapped.Select(item => item.Position));
            Assert.False(swapped[2].IsActive);

            // The same final code remains invalid even when its existing item is inactive.
            // SaveChanges is allowed inside the transaction; the deferred constraint rejects its commit.
            await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
            {
                Assert.True(catalog.UpdateManualItem(first, inactive.Code, first.TextEs, first.TextEn, first.DetailEs, first.DetailEn, first.Position).IsSuccess);
                await db.SaveChangesAsync(Ct);
                var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Ct));
                Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
                Assert.Equal("AK_BusinessFormCatalogItems_CatalogId_Code", error.ConstraintName);
            }
            db.ChangeTracker.Clear();
            Assert.Equivalent(swapped, await db.Set<BusinessFormCatalogItem>().AsNoTracking().OrderBy(item => item.Position).ToArrayAsync(Ct), strict: true);

            // Code identity is per catalog, so another catalog can use the reserved text.
            var name = new Dictionary<string, string> { ["es"] = "Otro catálogo", ["en"] = "Other catalog" };
            var other = BusinessFormCatalog.CreateManual(name, name);
            db.Add(other);
            db.Add(other.AddManualItem("RESERVED", "Otro", "Other", null, null, 1).Value);
            await db.SaveChangesAsync(Ct);
            var beforeDown = await HistoricalRowsAsync(db);

            await migrator.MigrateAsync(previousMigration, Ct);
            Assert.True(await ImmediateCodeIndexExistsAsync(db));
            Assert.False(await DeferredCodeConstraintExistsAsync(db));
            Assert.Equal(beforeDown, await HistoricalRowsAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.True(await DeferredCodeConstraintExistsAsync(db));
            Assert.False(await ImmediateCodeIndexExistsAsync(db));
            Assert.Equal(beforeDown, await HistoricalRowsAsync(db));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(Ct);
        }
    }

    // Select only columns that existed before this migration, including typed audit values.
    private static Task<string[]> HistoricalRowsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('id', "Id", 'catalogId', "CatalogId", 'code', "Code", 'textEs', "TextEs", 'textEn', "TextEn",
            'detailEs', "DetailEs", 'detailEn', "DetailEn", 'position', "Position", 'isActive', "IsActive",
            'createdAtUtc', "CreatedAtUtc", 'createdBy', "CreatedBy", 'modifiedAtUtc', "ModifiedAtUtc", 'modifiedBy', "ModifiedBy")::text AS "Value"
        FROM "BusinessFormCatalogItems" ORDER BY "Id"
        """).ToArrayAsync(Ct);

    private static Task<bool> DeferredCodeConstraintExistsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<bool>("""
        SELECT EXISTS (SELECT 1 FROM pg_constraint
            WHERE conrelid = '"BusinessFormCatalogItems"'::regclass
                AND conname = 'AK_BusinessFormCatalogItems_CatalogId_Code'
                AND condeferrable AND condeferred) AS "Value"
        """).SingleAsync(Ct);

    private static Task<bool> ImmediateCodeIndexExistsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<bool>("""
        SELECT to_regclass('public."IX_BusinessFormCatalogItems_CatalogId_Code"') IS NOT NULL AS "Value"
        """).SingleAsync(Ct);
}
