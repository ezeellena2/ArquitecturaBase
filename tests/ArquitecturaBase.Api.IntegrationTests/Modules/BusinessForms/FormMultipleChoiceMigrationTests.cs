using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormMultipleChoiceMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Legacy_choices_keep_their_rows_and_seals_with_position_zero_through_up_down_up()
    {
        var connection = factory.NewDatabaseConnectionString("multiple_choice_history");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormMultipleChoiceMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var history = await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct);
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT "Position" AS "Value" FROM "BusinessFormRecordValues" WHERE "ChoiceId" = {seed.ItemId}""").SingleAsync(Ct));
            await migrator.MigrateAsync(FormMultipleChoiceMigrationFixture.Before, Ct);
            Assert.Equal(history, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await FormCatalogValueMigrationFixture.HistoricalRowsAsync(db, Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Multiple_rows_are_bounded_by_position_and_references_are_unique_per_selected_item()
    {
        var connection = factory.NewDatabaseConnectionString("multiple_choice_indexes");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormMultipleChoiceMigrationFixture.Before, Ct);
            var catalog = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(cancellationToken: Ct);
            var captured = await FormMultipleChoiceMigrationFixture.AddCapturedAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, 2, Ct);
            var positions = await db.Database.SqlQuery<int>($"""
                SELECT "Position" AS "Value" FROM "BusinessFormRecordValues" WHERE "RecordId" = {captured.RecordId} ORDER BY "Position"
                """).ToArrayAsync(Ct);
            Assert.Equal([0, 1], positions);
            var duplicatePosition = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormRecordValues" SET "Position" = 0 WHERE "RecordId" = {captured.RecordId} AND "Position" = 1
                """, Ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicatePosition.SqlState);
            var duplicateItem = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormRecordReferences" SET "ItemId" = {captured.ItemId} WHERE "RecordId" = {captured.RecordId} AND "Id" <> {captured.ReferenceId}
                """, Ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateItem.SqlState);
            foreach (var position in new[] { -1, 20 })
            {
                var outOfRange = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                    UPDATE "BusinessFormRecordValues" SET "Position" = {position} WHERE "Id" = {captured.ValueId}
                    """, Ct));
                Assert.Equal("CK_BusinessFormRecordValues_Position", outOfRange.ConstraintName);
            }
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("single-multiChoice")]
    [InlineData("empty-multiChoice")]
    [InlineData("positive-position")]
    [InlineData("multiple-references")]
    public async Task Down_refuses_unrepresentable_facts_including_one_multiple_selection_at_position_zero(string fact)
    {
        var connection = factory.NewDatabaseConnectionString("multiple_choice_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormMultipleChoiceMigrationFixture.Before, Ct);
            var catalog = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(cancellationToken: Ct);
            if (fact == "single-multiChoice")
            {
                var captured = await FormMultipleChoiceMigrationFixture.AddCapturedAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, 1, Ct);
                Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT "Position" AS "Value" FROM "BusinessFormRecordValues" WHERE "Id" = {captured.ValueId}""").SingleAsync(Ct));
                Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::integer AS "Value" FROM "BusinessFormRecordReferences" WHERE "RecordId" = {captured.RecordId}""").SingleAsync(Ct));
            }
            else if (fact == "empty-multiChoice")
            {
                var captured = await FormMultipleChoiceMigrationFixture.AddCapturedAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, 0, Ct);
                Assert.Null(captured.ValueId); Assert.Null(captured.ReferenceId); Assert.Null(captured.ItemId);
                Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::integer AS "Value" FROM "BusinessFormRecordValues" WHERE "RecordId" = {captured.RecordId}""").SingleAsync(Ct));
                Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::integer AS "Value" FROM "BusinessFormRecordReferences" WHERE "RecordId" = {captured.RecordId}""").SingleAsync(Ct));
                Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT jsonb_array_length("ValuesJson" -> 0 -> 'value') AS "Value" FROM "BusinessFormRecords" WHERE "Id" = {captured.RecordId}""").SingleAsync(Ct));
            }
            else if (fact == "positive-position")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormRecordValues" SET "Position" = 1 WHERE "ChoiceId" = {catalog.ItemId}""", Ct);
            else
            {
                var referenceId = Guid.CreateVersion7(); var itemId = Guid.CreateVersion7();
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO "BusinessFormRecordReferences" ("Id", "RecordId", "DefinitionId", "FieldId", "CatalogId", "CatalogRevision", "ItemId", "Text", "CatalogName")
                    SELECT {referenceId}, "RecordId", "DefinitionId", "FieldId", "CatalogId", "CatalogRevision", {itemId}, "Text", "CatalogName"
                    FROM "BusinessFormRecordReferences" WHERE "Id" = {catalog.ReferenceId}
                    """, Ct);
            }
            var original = await FormMultipleChoiceMigrationFixture.AllCapturedRowsAsync(db, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(FormMultipleChoiceMigrationFixture.Before, Ct));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(original, await FormMultipleChoiceMigrationFixture.AllCapturedRowsAsync(db, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
