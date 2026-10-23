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
public sealed class FormCatalogFieldsMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Catalog_fields_migration_preserves_history_and_adds_only_empty_or_null_legacy_values()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_fields_history");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var original = await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct);
            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.WithFields, Ct);

            Assert.Equal(original, await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct));
            // The intermediate schema is read through its own columns, not the evolving EF model.
            var catalog = await db.Set<BusinessFormCatalog>().Where(value => value.Id == seed.CatalogId)
                .Select(value => new { value.FieldsJson, value.PresentationJson }).SingleAsync(Ct);
            Assert.Null(catalog.FieldsJson);
            Assert.Null(catalog.PresentationJson);
            Assert.Equal("{}", await db.Set<BusinessFormCatalogItem>().Where(value => value.Id == seed.ItemId)
                .Select(value => value.FieldValuesJson).SingleAsync(Ct));
            Assert.Null(await db.Set<BusinessFormRecordReference>().Where(value => value.Id == seed.ReferenceId)
                .Select(value => value.FieldValuesJson).SingleAsync(Ct));
            var index = await db.Database.SqlQuery<string>($"""
                SELECT indexdef AS "Value" FROM pg_indexes
                WHERE schemaname = 'public' AND indexname = 'IX_BusinessFormCatalogItems_FieldValuesJson'
                """).SingleAsync(Ct);
            Assert.Contains("USING gin", index, StringComparison.Ordinal);
            Assert.Contains("jsonb_path_ops", index, StringComparison.Ordinal);
            const string emptyFields = "[]";
            const string filters = "{}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormCatalogs" SET "Kind" = 'Filters', "FiltersJson" = CAST({filters} AS jsonb),
                    "FieldsJson" = CAST({emptyFields} AS jsonb) WHERE "Id" = {seed.CatalogId}
                """, Ct));
            Assert.Equal("CK_BusinessFormCatalogs_ManualFields", error.ConstraintName);

            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.Before, Ct);
            Assert.Equal(original, await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct));
            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.WithFields, Ct);
            Assert.Equal(original, await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("presentation")]
    [InlineData("item")]
    [InlineData("reference")]
    public async Task Down_refuses_any_catalog_field_metadata_or_captured_value_before_removing_schema(string target)
    {
        var connection = factory.NewDatabaseConnectionString("catalog_fields_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.WithFields, Ct);
            const string emptyObject = "{}";
            const string itemFields = """{"field":"value"}""";
            if (target == "fields")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogs" SET "FieldsJson" = '[]'::jsonb WHERE "Id" = {seed.CatalogId}""", Ct);
            else if (target == "presentation")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogs" SET "PresentationJson" = CAST({emptyObject} AS jsonb) WHERE "Id" = {seed.CatalogId}""", Ct);
            else if (target == "item")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogItems" SET "FieldValuesJson" = CAST({itemFields} AS jsonb) WHERE "Id" = {seed.ItemId}""", Ct);
            else
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormRecordReferences" SET "FieldValuesJson" = '[]'::jsonb WHERE "Id" = {seed.ReferenceId}""", Ct);

            var original = await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct);
            var beforeDown = await db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_array(catalog."FieldsJson", catalog."PresentationJson", item."FieldValuesJson", reference."FieldValuesJson")::text AS "Value"
                FROM "BusinessFormCatalogs" catalog JOIN "BusinessFormCatalogItems" item ON item."CatalogId" = catalog."Id"
                JOIN "BusinessFormRecordReferences" reference ON reference."CatalogId" = catalog."Id" WHERE catalog."Id" = {seed.CatalogId}
                """).SingleAsync(Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(FormCatalogFieldsMigrationFixture.Before, Ct));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(original, await FormCatalogFieldsMigrationFixture.HistoricalRowsAsync(db, Ct));
            var afterDown = await db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_array(catalog."FieldsJson", catalog."PresentationJson", item."FieldValuesJson", reference."FieldValuesJson")::text AS "Value"
                FROM "BusinessFormCatalogs" catalog JOIN "BusinessFormCatalogItems" item ON item."CatalogId" = catalog."Id"
                JOIN "BusinessFormRecordReferences" reference ON reference."CatalogId" = catalog."Id" WHERE catalog."Id" = {seed.CatalogId}
                """).SingleAsync(Ct);
            Assert.Equal(beforeDown, afterDown);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }
}
