using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormRecordCatalogFieldsMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_migration_fills_the_union_of_sealed_catalog_fields_from_the_existing_responses()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb",
            factory.NewDatabaseConnectionString("record_catalog_fields_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormPersonDataMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await FormPersonDataMigrationFixture.SeedCatalogFieldsAsync(db, seed, Ct);

            await migrator.MigrateAsync(cancellationToken: Ct);

            var rows = await db.Database.SqlQueryRaw<string>("""
                SELECT concat_ws('|', "CatalogId", "FieldId", "Key", "NameJson"->>'es', "Type", "Role", "Position", "RecordNumber") AS "Value"
                FROM "BusinessFormRecordCatalogFields"
                """).ToArrayAsync(Ct);
            Assert.Equal([$"{seed.CatalogId}|0199f0a1-0000-7000-8000-000000000001|days|Días históricos|integer|data|0|1"], rows);
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
