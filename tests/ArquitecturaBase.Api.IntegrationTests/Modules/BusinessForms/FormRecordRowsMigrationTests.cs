using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormRecordRowsMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Captured_history_keeps_its_facts_in_row_zero_through_up_down_up()
    {
        var connection = factory.NewDatabaseConnectionString("record_rows_history");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var rejection = await FormRecordRowsMigrationFixture.AddRejectionAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var history = await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct);

            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.WithRows, Ct);

            Assert.Equal(history, await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct));
            Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT "Row" AS "Value" FROM "BusinessFormRecordValues" WHERE "ChoiceId" = {seed.ItemId}""").SingleAsync(Ct));
            Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT "Row" AS "Value" FROM "BusinessFormRecordReferences" WHERE "Id" = {seed.ReferenceId}""").SingleAsync(Ct));
            Assert.Null(await db.Database.SqlQuery<int?>($"""SELECT "RejectedRow" AS "Value" FROM "BusinessFormCaptureReceipts" WHERE "Id" = {rejection}""").SingleAsync(Ct));
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);
            Assert.Equal(history, await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct));
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.WithRows, Ct);
            Assert.Equal(history, await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task The_same_option_repeats_across_items_and_each_item_stays_within_its_bounds()
    {
        var connection = factory.NewDatabaseConnectionString("record_rows_indexes");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);
            var catalog = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.WithRows, Ct);
            var block = await FormRecordRowsMigrationFixture.AddBlockAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, 2, Ct);
            var recordId = block.RecordId!.Value;

            var rows = await db.Database.SqlQuery<int>($"""
                SELECT "Row" AS "Value" FROM "BusinessFormRecordValues" WHERE "RecordId" = {recordId} AND "FieldId" = {block.QuantityFieldId} ORDER BY "Row"
                """).ToArrayAsync(Ct);
            Assert.Equal([0, 1], rows);
            var batch = await scope.ServiceProvider.GetRequiredService<IFormRecordReader>().FindBatchAsync([block.SourceReceiptId!.Value],
                new Dictionary<Guid, FormResponseVersionRow> { [block.VersionId] = new(block.VersionId, 1, block.NameJson, block.SchemaJson, "es", ["es"]) }, Ct);
            var references = Assert.Single(batch.Rows).References!.OrderBy(reference => reference.Row).ToArray();
            Assert.Equal([0, 1], references.Select(reference => reference.Row));
            Assert.All(references, reference => Assert.Equal(catalog.ItemId, reference.ItemId));

            var duplicateValue = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormRecordValues" SET "Row" = 0 WHERE "RecordId" = {recordId} AND "FieldId" = {block.QuantityFieldId} AND "Row" = 1
                """, Ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateValue.SqlState);
            var duplicateReference = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "BusinessFormRecordReferences" SET "Row" = 0 WHERE "RecordId" = {recordId} AND "Row" = 1
                """, Ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateReference.SqlState);
            foreach (var row in new[] { -1, 100 })
            {
                var value = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                    UPDATE "BusinessFormRecordValues" SET "Row" = {row} WHERE "RecordId" = {recordId} AND "FieldId" = {block.QuantityFieldId} AND "Row" = 1
                    """, Ct));
                Assert.Equal("CK_BusinessFormRecordValues_Row", value.ConstraintName);
                var reference = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                    UPDATE "BusinessFormRecordReferences" SET "Row" = {row} WHERE "RecordId" = {recordId} AND "Row" = 1
                    """, Ct));
                Assert.Equal("CK_BusinessFormRecordReferences_Row", reference.ConstraintName);
            }
            var rejection = await FormRecordRowsMigrationFixture.AddRejectionAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCaptureReceipts" SET "RejectedRow" = 99 WHERE "Id" = {rejection}""", Ct);
            var tooLarge = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormCaptureReceipts" SET "RejectedRow" = 100 WHERE "Id" = {rejection}""", Ct));
            var negative = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormCaptureReceipts" SET "RejectedRow" = -1 WHERE "Id" = {rejection}""", Ct));
            var withoutQuestion = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "BusinessFormCaptureReceipts" SET "RejectedFieldId" = NULL WHERE "Id" = {rejection}""", Ct));
            Assert.All(new[] { tooLarge, negative, withoutQuestion },
                error => Assert.Equal("CK_BusinessFormCaptureReceipts_RejectedRow", error.ConstraintName));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("block-response")]
    [InlineData("value-row")]
    [InlineData("reference-row")]
    [InlineData("rejected-row")]
    public async Task Down_refuses_the_facts_the_previous_schema_cannot_read(string fact)
    {
        var connection = factory.NewDatabaseConnectionString("record_rows_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);
            var catalog = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.WithRows, Ct);
            if (fact == "block-response")
            {
                // A response without items has no row of its own: the version with a block is what the previous image cannot read.
                var block = await FormRecordRowsMigrationFixture.AddBlockAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, 0, Ct);
                Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::integer AS "Value" FROM "BusinessFormRecordValues" WHERE "RecordId" = {block.RecordId}""").SingleAsync(Ct));
            }
            else if (fact == "value-row")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormRecordValues" SET "Row" = 1 WHERE "ChoiceId" = {catalog.ItemId}""", Ct);
            else if (fact == "reference-row")
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormRecordReferences" SET "Row" = 1 WHERE "Id" = {catalog.ReferenceId}""", Ct);
            else
            {
                var rejection = await FormRecordRowsMigrationFixture.AddRejectionAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCaptureReceipts" SET "RejectedRow" = 0 WHERE "Id" = {rejection}""", Ct);
            }
            var original = await FormRecordRowsMigrationFixture.AllFactsAsync(db, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(original, await FormRecordRowsMigrationFixture.AllFactsAsync(db, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Down_keeps_blocks_in_drafts_and_versions_without_responses()
    {
        var connection = factory.NewDatabaseConnectionString("record_rows_blocks_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);
            var catalog = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.WithRows, Ct);
            await FormRecordRowsMigrationFixture.AddBlockAsync(db, catalog, factory.Clock.GetUtcNow().UtcDateTime, null, Ct);
            var history = await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct);

            await migrator.MigrateAsync(FormRecordRowsMigrationFixture.Before, Ct);

            Assert.Equal(history, await FormRecordRowsMigrationFixture.HistoryAsync(db, Ct));
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::integer AS "Value" FROM "BusinessFormVersions" WHERE jsonb_path_exists("SchemaJson", '$.questions[*] ? (@.type == "group")')
                """).SingleAsync(Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
