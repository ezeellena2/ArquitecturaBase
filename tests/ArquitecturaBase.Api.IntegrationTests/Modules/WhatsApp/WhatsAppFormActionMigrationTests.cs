using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormActionMigrationTests(ApiFactory factory)
{
    private const string Before = "20261016190329_AddBusinessFormPdfDesigns";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rollback_discards_system_outgoing_and_redacts_its_history_without_changing_manual_sends_or_receipts()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:appdb", factory.NewDatabaseConnectionString("form_action_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var contactId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var manualMessageId = Guid.NewGuid();
        var manualOutgoingId = Guid.NewGuid();
        var manualRequestId = Guid.NewGuid();
        var manualReceiptId = Guid.NewGuid();

        try
        {
            await migrator.MigrateAsync(Before, Ct);
            // The previous schema has no Origin and still requires an operator on every outgoing row.
            // Outgoing Kind is an integer; history Kind, Direction, Purpose, State and receipt Status use text.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppContacts"
                    ("Id", "WaId", "ProfileName", "LastInboundAtUtc", "LastInboundSequence", "Mode", "ModeRevision", "LinkRevision")
                VALUES ({contactId}, '5493415550190', 'Migration history', {now}, 0, 'Manual', 1, 0);
                INSERT INTO "WhatsAppMessages"
                    ("Id", "ContactId", "Direction", "WaMessageId", "Kind", "Body", "OccurredAtUtc",
                     "InboundSequence", "OperatorUserId", "Purpose", "Status", "StatusAtUtc")
                VALUES ({manualMessageId}, {contactId}, 'Outbound', 'wamid.migration-manual', 'Text',
                    'Manual text that must survive', {now}, 0, {actorId}, 'Business', 'Read', {now});
                INSERT INTO "WhatsAppOutgoingMessages"
                    ("Id", "OperatorUserId", "ContactId", "RequestId", "Body", "RequestedAtUtc", "StartedAtUtc", "State", "Kind")
                VALUES ({manualOutgoingId}, {actorId}, {contactId}, {manualRequestId},
                    'Manual uncertain text that must survive', {now}, {now}, 'Uncertain', {(int)WhatsAppOutgoingMessageKind.Text});
                INSERT INTO "WhatsAppDeliveryReceipts"
                    ("Id", "WaMessageId", "Status", "OccurredAtUtc", "ErrorCode")
                VALUES ({manualReceiptId}, 'wamid.migration-manual', 'Read', {now}, NULL);
                """, Ct);

            await db.Database.MigrateAsync(Ct);
            Assert.Equal(WhatsAppMessageOrigin.Conversation,
                (await db.Set<WhatsAppMessage>().SingleAsync(item => item.Id == manualMessageId, Ct)).Origin);
            Assert.Equal(actorId,
                (await db.Set<WhatsAppOutgoingMessage>().SingleAsync(item => item.Id == manualOutgoingId, Ct)).OperatorUserId);

            var recipientId = Guid.NewGuid();
            var pending = WhatsAppOutgoingMessage.CreateFormAction(
                recipientId, contactId, Guid.NewGuid(), "Pending form value", now);
            var uncertain = WhatsAppOutgoingMessage.CreateFormAction(
                recipientId, contactId, Guid.NewGuid(), "Private PDF caption", now,
                new("private/migration.pdf", "Private receipt.pdf", "application/pdf", 80, new string('a', 64)));
            var uncertainReservation = uncertain.ReserveFormAction(now);
            Assert.True(uncertain.MarkFormActionMessageEffect(uncertainReservation, now));
            uncertain.MarkUncertain();
            var accepted = WhatsAppOutgoingMessage.CreateFormAction(
                recipientId, contactId, Guid.NewGuid(), "Accepted form value", now);
            var acceptedReservation = accepted.ReserveFormAction(now);
            Assert.True(accepted.MarkFormActionMessageEffect(acceptedReservation, now));
            accepted.Accept("wamid.migration-action");
            var actionMessage = WhatsAppMessage.Outbound(
                contactId, "wamid.migration-action", WhatsAppMessageKind.Document, "Sensitive form answer: 125500", now);
            actionMessage.AttributeFormAction();
            actionMessage.ApplyStatus(WhatsAppMessageStatus.Delivered, now, null);
            var actionReceipt = WhatsAppDeliveryReceipt.Create(
                "wamid.migration-action", WhatsAppMessageStatus.Delivered, now, null);
            db.AddRange(pending, uncertain, accepted, actionMessage, actionReceipt);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            Assert.Equal(3, await db.Set<WhatsAppOutgoingMessage>()
                .CountAsync(item => item.Origin == WhatsAppMessageOrigin.FormAction && item.OperatorUserId == null, Ct));

            await migrator.MigrateAsync(Before, Ct);
            db.ChangeTracker.Clear();

            // Assertions after Down use only the previous schema; the current EF entity expects removed columns.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                """SELECT count(*)::integer AS "Value" FROM "WhatsAppOutgoingMessages" """).SingleAsync(Ct));
            Assert.Equal(actorId, await db.Database.SqlQuery<Guid>($"""
                SELECT "OperatorUserId" AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {manualOutgoingId}
                """).SingleAsync(Ct));
            Assert.Equal(manualRequestId, await db.Database.SqlQuery<Guid>($"""
                SELECT "RequestId" AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {manualOutgoingId}
                """).SingleAsync(Ct));
            Assert.Equal("Uncertain", await db.Database.SqlQuery<string>($"""
                SELECT "State" AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {manualOutgoingId}
                """).SingleAsync(Ct));
            Assert.Equal("Manual uncertain text that must survive", await db.Database.SqlQuery<string>($"""
                SELECT "Body" AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {manualOutgoingId}
                """).SingleAsync(Ct));
            Assert.Equal("Manual text that must survive", await db.Database.SqlQuery<string>($"""
                SELECT "Body" AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {manualMessageId}
                """).SingleAsync(Ct));
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("Body" IS NULL AND "WaMessageId" = 'wamid.migration-action'
                    AND "Kind" = 'Media' AND "Status" = 'Delivered' AND "OccurredAtUtc" = {now}) AS "Value"
                FROM "WhatsAppMessages" WHERE "Id" = {actionMessage.Id}
                """).SingleAsync(Ct));
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
                """SELECT count(*)::integer AS "Value" FROM "WhatsAppMessages" """).SingleAsync(Ct));
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
                """SELECT count(*)::integer AS "Value" FROM "WhatsAppDeliveryReceipts" """).SingleAsync(Ct));
            Assert.Equal(manualReceiptId, await db.Database.SqlQueryRaw<Guid>("""
                SELECT "Id" AS "Value" FROM "WhatsAppDeliveryReceipts" WHERE "WaMessageId" = 'wamid.migration-manual'
                """).SingleAsync(Ct));
            Assert.Equal("Read", await db.Database.SqlQuery<string>($"""
                SELECT "Status" AS "Value" FROM "WhatsAppDeliveryReceipts" WHERE "Id" = {manualReceiptId}
                """).SingleAsync(Ct));
            Assert.Equal("Delivered", await db.Database.SqlQuery<string>($"""
                SELECT "Status" AS "Value" FROM "WhatsAppDeliveryReceipts" WHERE "Id" = {actionReceipt.Id}
                """).SingleAsync(Ct));
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::integer AS "Value" FROM information_schema.columns
                WHERE table_name IN ('WhatsAppMessages', 'WhatsAppOutgoingMessages') AND column_name = 'Origin'
                """).SingleAsync(Ct));

            await db.Database.MigrateAsync(Ct);
            db.ChangeTracker.Clear();
            Assert.Equal("Manual text that must survive",
                (await db.Set<WhatsAppMessage>().SingleAsync(item => item.Id == manualMessageId, Ct)).Body);
            var restoredAction = await db.Set<WhatsAppMessage>().SingleAsync(item => item.Id == actionMessage.Id, Ct);
            Assert.Null(restoredAction.Body);
            Assert.Equal(WhatsAppMessageKind.Media, restoredAction.Kind);
            Assert.False(await db.Set<WhatsAppOutgoingMessage>()
                .AnyAsync(item => item.Origin == WhatsAppMessageOrigin.FormAction, Ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(Ct);
        }
    }
}
