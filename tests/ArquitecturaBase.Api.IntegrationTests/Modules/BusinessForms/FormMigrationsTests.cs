using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Lo que las migraciones del módulo dejan en una base nueva y dos de sus recorridos de ida y vuelta. Viven con el módulo
/// para que las pruebas de migraciones del núcleo no nombren sus tablas.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormMigrationsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_leave_the_catalog_position_key_deferred_and_drop_the_catalog_users_view()
    {
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("formmigrations")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            var positionConstraintIsDeferred = await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT condeferrable AND condeferred AS "Value"
                FROM pg_constraint
                WHERE conname = 'AK_BusinessFormCatalogItems_CatalogId_Position'
                """).SingleAsync(Ct);
            Assert.True(positionConstraintIsDeferred);
            var userViewExists = await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('public."BusinessFormCatalogUsers"') IS NOT NULL AS "Value"
                """).SingleAsync(Ct);
            Assert.False(userViewExists);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Catalog_user_view_is_removed_and_restored_with_its_migration()
    {
        const string previousMigration = "20261012211548_AddBusinessFormCatalogResponses";
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("catalogview")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(previousMigration, Ct);
            Assert.True(await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('public."BusinessFormCatalogUsers"') IS NOT NULL AS "Value"
                """).SingleAsync(Ct));

            await dbContext.Database.MigrateAsync(Ct);
            Assert.False(await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('public."BusinessFormCatalogUsers"') IS NOT NULL AS "Value"
                """).SingleAsync(Ct));

            await migrator.MigrateAsync(previousMigration, Ct);
            Assert.True(await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('public."BusinessFormCatalogUsers"') IS NOT NULL AS "Value"
                """).SingleAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Retired_identity_migration_preserves_existing_json_ids_in_both_directions()
    {
        const string previousMigration = "20261010171711_AddFormVersionPresentationAndContactLinkRevision";
        var definitionId = Guid.CreateVersion7();
        var fieldId = Guid.CreateVersion7();
        var choiceId = Guid.CreateVersion7();
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("retiredids")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(previousMigration, Ct);
            await dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO "FormDefinitions"
                    ("Id", "Name", "NameJson", "Prefix", "PrefixLocked", "Revision", "Culture", "SubmitLabelJson",
                     "SchemaJson", "RetiredIdentityLedgerJson", "QuestionCount", "CreatedAtUtc")
                VALUES ({0}, 'Migration test', '{{"es":"Migration test"}}'::jsonb, 'MIGTEST', false, 1, 'es',
                    '{{"es":"Enviar"}}'::jsonb, '{{"format":1,"fields":[]}}'::jsonb,
                    jsonb_build_object('fields', jsonb_build_array({1}::text), 'choices', jsonb_build_array({2}::text)), 0, now())
                """, [definitionId, fieldId, choiceId], Ct);

            await dbContext.Database.MigrateAsync(Ct);

            var migratedCount = await dbContext.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS "Value" FROM "BusinessFormRetiredIdentities" WHERE "DefinitionId" = {0}
                """, definitionId).SingleAsync(Ct);
            Assert.Equal(2, migratedCount);

            await migrator.MigrateAsync(previousMigration, Ct);
            var restoredLedger = await dbContext.Database.SqlQueryRaw<string>("""
                SELECT "RetiredIdentityLedgerJson"::text AS "Value" FROM "FormDefinitions" WHERE "Id" = {0}
                """, definitionId).SingleAsync(Ct);
            using var document = System.Text.Json.JsonDocument.Parse(restoredLedger);
            Assert.Equal(fieldId.ToString(), document.RootElement.GetProperty("fields")[0].GetString());
            Assert.Equal(choiceId.ToString(), document.RootElement.GetProperty("choices")[0].GetString());
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }
}
