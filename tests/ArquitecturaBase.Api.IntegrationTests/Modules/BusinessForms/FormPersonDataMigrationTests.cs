using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormPersonDataMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Person_data_up_down_up_preserves_existing_catalog_snapshots_and_does_not_require_an_identity_account()
    {
        var connection = factory.NewDatabaseConnectionString("person_data_history");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await FormPersonDataMigrationFixture.SeedCatalogFieldsAsync(db, seed, Ct);
            var history = await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct);
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.WithPeople, Ct);
            Assert.Equal(history, await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct));
            // The intermediate schema is read through its own columns, not the evolving EF model.
            Assert.Empty(await db.Set<BusinessFormPersonAttribute>().Select(value => value.Id).ToArrayAsync(Ct));
            Assert.Empty(await db.Set<BusinessFormPersonData>().Select(value => value.Id).ToArrayAsync(Ct));
            var userId = Guid.NewGuid(); var dataId = Guid.NewGuid(); var now = factory.Clock.GetUtcNow().UtcDateTime;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormPersonData" ("Id", "UserId", "Revision", "CreatedAtUtc") VALUES ({dataId}, {userId}, 1, {now});
                """, Ct);
            Assert.Equal("{}", await db.Set<BusinessFormPersonData>().Select(value => value.ValuesJson).SingleAsync(Ct));
            Assert.False(await db.Users.AnyAsync(user => user.Id == userId, Ct));
            var attributeId = Guid.NewGuid(); const string name = "{\"es\":\"Zona\"}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormPersonAttributes" ("Id", "Key", "NameJson", "Type", "Position", "IsActive", "Revision", "CreatedAtUtc")
                VALUES ({attributeId}, 'zone', CAST({name} AS jsonb), 'Catalog', 0, TRUE, 1, {now});
                """, Ct));
            Assert.Equal("CK_BusinessFormPersonAttributes_Source", error.ConstraintName);
            await db.Database.ExecuteSqlAsync($"""DELETE FROM "BusinessFormPersonData" WHERE "Id" = {dataId}""", Ct);
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.Before, Ct);
            Assert.Equal(history, await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct));
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.WithPeople, Ct);
            Assert.Equal(history, await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Person_data_down_refuses_either_table_with_facts_before_removing_any_schema(bool attribute)
    {
        var connection = factory.NewDatabaseConnectionString("person_data_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await FormPersonDataMigrationFixture.SeedCatalogFieldsAsync(db, seed, Ct);
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.WithPeople, Ct);
            var history = await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct);
            var id = Guid.NewGuid(); var now = factory.Clock.GetUtcNow().UtcDateTime; const string name = "{\"es\":\"Zona\"}";
            if (attribute)
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO "BusinessFormPersonAttributes" ("Id", "Key", "NameJson", "Type", "Position", "IsActive", "Revision", "CreatedAtUtc")
                    VALUES ({id}, 'zone', CAST({name} AS jsonb), 'Text', 0, FALSE, 1, {now});
                    """, Ct);
            else
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO "BusinessFormPersonData" ("Id", "UserId", "Revision", "CreatedAtUtc") VALUES ({id}, {Guid.NewGuid()}, 1, {now});
                    """, Ct);
            var facts = await ReadPeopleAsync(db);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(FormPersonDataMigrationFixture.Before, Ct));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(facts, await ReadPeopleAsync(db));
            Assert.Equal(history, await FormPersonDataMigrationFixture.HistoricalRowsAsync(db, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private static Task<string[]> ReadPeopleAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'attribute', 'data', to_jsonb(r))::text AS "Value" FROM "BusinessFormPersonAttributes" r
        UNION ALL SELECT jsonb_build_object('table', 'person', 'data', to_jsonb(r))::text FROM "BusinessFormPersonData" r
        ORDER BY "Value"
        """).ToArrayAsync(Ct);
}
