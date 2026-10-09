using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAccessAndChatMigrationTests(ApiFactory factory)
{
    private const string Before = "20261002171814_FieldAppearanceDefaults";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_changes_only_bot_mode_revision_and_demonstrable_message_purpose()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("access_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var bot = Guid.NewGuid();
        var manual = Guid.NewGuid();
        var user = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var inbound = Guid.NewGuid();
        var outbound = Guid.NewGuid();
        var legacyAccess = Guid.NewGuid();
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(Before, Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppContacts" ("Id", "WaId", "UserIdentifier", "UserId", "ProfileName", "LastInboundAtUtc", "LastInboundSequence", "Mode", "ModeRevision")
                VALUES ({bot}, '5493515550000', 'existing.bsuid', {user}, 'Historia', {now}, 2, 'Bot', 7),
                       ({manual}, '5493515550001', NULL, NULL, 'Manual', {now}, 0, 'Manual', 8);
                INSERT INTO "WhatsAppMessages" ("Id", "ContactId", "Direction", "WaMessageId", "Kind", "Body", "OccurredAtUtc", "InboundSequence", "OperatorUserId", "ProcessedAtUtc", "Status", "StatusAtUtc")
                VALUES ({inbound}, {bot}, 'Inbound', 'wamid.history.in', 'Text', 'código WANT_TO_ENTER', {now}, 2, NULL, NULL, NULL, NULL),
                       ({outbound}, {bot}, 'Outbound', 'wamid.history.out', 'Text', 'Respuesta', {now}, 0, {actor}, NULL, 'Read', {now}),
                       ({legacyAccess}, {bot}, 'Outbound', 'wamid.history.code', 'Template', '[código]', {now}, 0, NULL, NULL, 'Delivered', {now});
                """, Ct);
            var pendingLink = LoginLink.Issue(user, new string('A', 64), now);
            db.Add(pendingLink);
            var read = WhatsAppReadState.Create(actor, bot);
            read.Advance(1);
            db.Add(read);
            db.Add(WhatsAppDeliveryReceipt.Create("wamid.early", WhatsAppMessageStatus.Delivered, now, null));
            var pending = WhatsAppOutgoingMessage.Create(actor, bot, Guid.NewGuid(), "Pendiente", now);
            var sending = WhatsAppOutgoingMessage.Create(actor, bot, Guid.NewGuid(), "En curso", now);
            sending.Start(now);
            var uncertain = WhatsAppOutgoingMessage.Create(actor, bot, Guid.NewGuid(), "Revisar", now);
            uncertain.Start(now);
            uncertain.MarkUncertain();
            db.AddRange(pending, sending, uncertain);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            await db.Database.MigrateAsync(Ct);

            var migrated = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == bot, Ct);
            Assert.Equal(WhatsAppConversationMode.Manual, migrated.Mode);
            Assert.Equal(8, migrated.ModeRevision);
            Assert.Equal(user, migrated.UserId);
            Assert.Equal("existing.bsuid", migrated.UserIdentifier);
            Assert.Equal(now, migrated.LastInboundAtUtc);
            Assert.Equal(2, migrated.LastInboundSequence);
            Assert.Equal(8, (await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == manual, Ct)).ModeRevision);
            var history = await db.Set<WhatsAppMessage>().ToListAsync(Ct);
            var received = Assert.Single(history, item => item.Id == inbound);
            Assert.Equal(WhatsAppMessagePurpose.Unclassified, received.Purpose);
            Assert.Null(received.AccessEventKind);
            Assert.Equal("código WANT_TO_ENTER", received.Body);
            Assert.Equal(2, received.InboundSequence);
            Assert.Null(received.ProcessedAtUtc);
            var sent = Assert.Single(history, item => item.Id == outbound);
            Assert.Equal(WhatsAppMessagePurpose.Business, sent.Purpose);
            Assert.Equal(WhatsAppMessageStatus.Read, sent.Status);
            Assert.Equal(now, sent.StatusAtUtc);
            Assert.Equal(actor, sent.OperatorUserId);
            var legacy = Assert.Single(history, item => item.Id == legacyAccess);
            Assert.Equal(WhatsAppMessagePurpose.Unclassified, legacy.Purpose);
            Assert.Equal("[código]", legacy.Body);
            Assert.Equal(WhatsAppMessageStatus.Delivered, legacy.Status);
            var preservedLink = await db.LoginLinks.SingleAsync(Ct);
            Assert.Equal(pendingLink.Id, preservedLink.Id);
            Assert.Equal(pendingLink.TokenHash, preservedLink.TokenHash);
            Assert.Equal(pendingLink.ExpiresAtUtc, preservedLink.ExpiresAtUtc);
            Assert.True(preservedLink.IsActive(now));
            Assert.Equal(1, (await db.Set<WhatsAppReadState>().SingleAsync(Ct)).LastReadSequence);
            Assert.Equal(WhatsAppMessageStatus.Delivered, (await db.Set<WhatsAppDeliveryReceipt>().SingleAsync(Ct)).Status);
            var operations = await db.Set<WhatsAppOutgoingMessage>().ToListAsync(Ct);
            Assert.Equal(WhatsAppOutgoingMessageState.Pending, Assert.Single(operations, item => item.Id == pending.Id).State);
            Assert.Equal(WhatsAppOutgoingMessageState.Sending, Assert.Single(operations, item => item.Id == sending.Id).State);
            var preserved = Assert.Single(operations, item => item.Id == uncertain.Id);
            Assert.Equal(WhatsAppOutgoingMessageState.Uncertain, preserved.State);
            Assert.Equal(uncertain.RequestId, preserved.RequestId);
            Assert.Equal("Revisar", preserved.Body);
            Assert.Equal(actor, preserved.OperatorUserId);

            var fresh = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppContacts" ("Id", "WaId", "LastInboundAtUtc") VALUES ({fresh}, '5493515550002', {now});
                """, Ct);
            Assert.Equal(WhatsAppConversationMode.Manual, (await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == fresh, Ct)).Mode);
            Assert.False(db.Database.HasPendingModelChanges());

            await db.GetService<IMigrator>().MigrateAsync(Before, Ct);
            db.ChangeTracker.Clear();
            Assert.All(await db.Set<WhatsAppContact>().ToListAsync(Ct), item => Assert.Equal(WhatsAppConversationMode.Manual, item.Mode));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
