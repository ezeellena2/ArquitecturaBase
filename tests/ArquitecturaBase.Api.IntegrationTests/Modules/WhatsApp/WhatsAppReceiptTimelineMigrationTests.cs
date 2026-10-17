using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppReceiptTimelineMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nullable_timeline_columns_leave_old_receipts_intact_in_both_directions()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:appdb", factory.NewDatabaseConnectionString("receipt_timeline_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrations = db.Database.GetMigrations().ToArray();
        var timeline = Assert.Single(migrations, name => name.EndsWith("_AddWhatsAppReceiptTimeline", StringComparison.Ordinal));
        var index = Array.IndexOf(migrations, timeline);
        Assert.True(index > 0);
        var before = migrations[index - 1];
        var migrator = db.GetService<IMigrator>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var receipts = new[] { "Sent", "Delivered", "Read", "Failed" }
            .Select(status => (Id: Guid.NewGuid(), Wamid: "wamid.timeline-" + Guid.NewGuid().ToString("N"), Status: status)).ToArray();
        try
        {
            await migrator.MigrateAsync(before, Ct);
            foreach (var receipt in receipts)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "WhatsAppDeliveryReceipts" ("Id", "WaMessageId", "Status", "OccurredAtUtc", "ErrorCode")
                    VALUES ({receipt.Id}, {receipt.Wamid}, {receipt.Status}, {now}, 130429)
                    """, Ct);
            await migrator.MigrateAsync(timeline, Ct);
            // No backfill: the reader projects the one old known status; later events preserve it in Apply.
            foreach (var receipt in receipts)
                Assert.True(await db.Database.SqlQuery<bool>($"""
                    SELECT ("Status" = {receipt.Status} AND "OccurredAtUtc" = {now} AND "ErrorCode" = 130429
                        AND "DeliveredAtUtc" IS NULL AND "ReadAtUtc" IS NULL) AS "Value"
                    FROM "WhatsAppDeliveryReceipts" WHERE "Id" = {receipt.Id}
                    """).SingleAsync(Ct));
            await migrator.MigrateAsync(before, Ct);
            foreach (var receipt in receipts)
                Assert.True(await db.Database.SqlQuery<bool>($"""
                    SELECT ("Status" = {receipt.Status} AND "OccurredAtUtc" = {now} AND "ErrorCode" = 130429) AS "Value"
                    FROM "WhatsAppDeliveryReceipts" WHERE "Id" = {receipt.Id}
                    """).SingleAsync(Ct));
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::integer AS "Value" FROM information_schema.columns
                WHERE table_name = 'WhatsAppDeliveryReceipts' AND column_name IN ('DeliveredAtUtc', 'ReadAtUtc')
                """).SingleAsync(Ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(Ct);
        }
    }
}
