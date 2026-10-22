using System.Net;
using System.Net.Http.Json;
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
public sealed class FormCatalogValueMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Legacy_catalogs_and_captured_rows_survive_up_down_and_a_real_http_code_swap()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_values_history");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormCatalogValueMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var original = await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct);
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(original, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            Assert.Equal(BusinessFormCatalogValueType.Text, await db.Set<BusinessFormCatalog>().Where(value => value.Id == seed.CatalogId)
                .Select(value => value.ValueType).SingleAsync(Ct));
            Assert.Null(await db.Set<BusinessFormCatalogItem>().Where(value => value.Id == seed.ItemId).Select(value => value.Value).SingleAsync(Ct));
            var reference = await db.Set<BusinessFormRecordReference>().SingleAsync(value => value.Id == seed.ReferenceId, Ct);
            Assert.Null(reference.Value); Assert.Null(reference.ValueType);
            var unknownType = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormCatalogs" SET "ValueType" = 'Unknown' WHERE "Id" = {seed.CatalogId}""", Ct));
            Assert.Equal("CK_BusinessFormCatalogs_ValueType", unknownType.ConstraintName);
            const string filters = "{}";
            var typedFilter = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormCatalogs" SET "Kind" = 'Filters', "FiltersJson" = CAST({filters} AS jsonb), "ValueType" = 'Decimal' WHERE "Id" = {seed.CatalogId}""", Ct));
            Assert.Equal("CK_BusinessFormCatalogs_ValueType", typedFilter.ConstraintName);
            var missingType = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormRecordReferences" SET "Value" = '1500' WHERE "Id" = {seed.ReferenceId}""", Ct));
            Assert.Equal("CK_BusinessFormRecordReferences_Value", missingType.ConstraintName);
            var missingValue = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormRecordReferences" SET "ValueType" = 'Decimal' WHERE "Id" = {seed.ReferenceId}""", Ct));
            Assert.Equal("CK_BusinessFormRecordReferences_Value", missingValue.ConstraintName);

            var actor = await FormCatalogValueMigrationFixture.CreateOperatorAsync(scope.ServiceProvider, Ct);
            using var client = host.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
            var texts = new { es = "Intercambio real", en = "Real swap" };
            using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
            {
                name = texts, description = texts,
                items = new[] { Item("first", null, "A"), Item("second", null, "B") },
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.ReadJsonAsync();
            var catalogId = body.GetProperty("catalog").GetProperty("id").GetGuid();
            var firstId = body.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
            var secondId = body.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
            using var updated = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
            {
                expectedRevision = 1, name = texts, description = texts,
                items = new[] { Item("first", firstId, "B"), Item("second", secondId, "A") },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await updated.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());
            var swapped = await db.Set<BusinessFormCatalogItem>().AsNoTracking().Where(value => value.CatalogId == catalogId)
                .OrderBy(value => value.Position).Select(value => new { value.Id, value.Code, value.Position, value.Value }).ToArrayAsync(Ct);
            Assert.Equal([firstId, secondId], swapped.Select(value => value.Id));
            Assert.Equal(["B", "A"], swapped.Select(value => value.Code));
            Assert.Equal([1, 2], swapped.Select(value => value.Position));
            Assert.All(swapped, value => Assert.Null(value.Value));
            var beforeDown = await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct);
            await migrator.MigrateAsync(FormCatalogValueMigrationFixture.Before, Ct);
            Assert.Equal(beforeDown, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(beforeDown, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("item")]
    [InlineData("reference")]
    public async Task Down_refuses_typed_catalogs_or_stored_values_before_dropping_any_column(string target)
    {
        var connection = factory.NewDatabaseConnectionString("catalog_values_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormCatalogValueMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(cancellationToken: Ct);
            if (target == "reference")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormRecordReferences" SET "Value" = '1500', "ValueType" = 'Decimal' WHERE "Id" = {seed.ReferenceId}""", Ct);
            else if (target == "item")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogItems" SET "Value" = '1500' WHERE "Id" = {seed.ItemId}""", Ct);
            else
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogs" SET "ValueType" = 'Decimal' WHERE "Id" = {seed.CatalogId}""", Ct);
            var beforeDown = await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(FormCatalogValueMigrationFixture.Before, Ct));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(beforeDown, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            Assert.Equal(target == "catalog" ? "Decimal" : "Text", await db.Database.SqlQuery<string>($"""SELECT "ValueType" AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {seed.CatalogId}""").SingleAsync(Ct));
            Assert.Equal(target == "reference", await db.Database.SqlQuery<bool>($"""SELECT "Value" IS NOT NULL AND "ValueType" IS NOT NULL AS "Value" FROM "BusinessFormRecordReferences" WHERE "Id" = {seed.ReferenceId}""").SingleAsync(Ct));
            Assert.Equal(target == "item", await db.Database.SqlQuery<bool>($"""SELECT "Value" IS NOT NULL AS "Value" FROM "BusinessFormCatalogItems" WHERE "Id" = {seed.ItemId}""").SingleAsync(Ct));
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    private static object Item(string localId, Guid? itemId, string code) => new
    {
        localId, itemId, code, text = new { es = code, en = code }, detail = new { es = (string?)null, en = (string?)null }, isActive = true,
    };
}
