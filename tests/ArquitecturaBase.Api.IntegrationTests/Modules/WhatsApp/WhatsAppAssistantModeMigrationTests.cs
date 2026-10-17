using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantModeMigrationTests(ApiFactory factory)
{
    private const string Before = "20261017191303_AddBusinessFormOfferWhen";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_bot_chats_become_manual_without_changing_history_or_enabling_assistance()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_mode_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var bot = Guid.NewGuid(); var manual = Guid.NewGuid(); var message = Guid.NewGuid(); var pending = Guid.NewGuid();
        var actor = Guid.NewGuid(); var request = Guid.NewGuid();
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(Before, Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppContacts" ("Id", "WaId", "ProfileName", "LastInboundAtUtc", "LastInboundSequence", "Mode", "ModeRevision")
                VALUES ({bot}, '5493415550100', 'Legacy fictional contact', {now}, 1, 'Bot', 7),
                       ({manual}, '5493415550101', 'Manual fictional contact', {now}, 0, 'Manual', 8);
                INSERT INTO "WhatsAppMessages" ("Id", "ContactId", "Direction", "WaMessageId", "Kind", "Body", "OccurredAtUtc", "InboundSequence", "Purpose")
                VALUES ({message}, {bot}, 'Inbound', 'wamid.assistant.mode.migration', 'Text', 'Fictitious prior message', {now}, 1, 'Business');
                INSERT INTO "WhatsAppOutgoingMessages" ("Id", "OperatorUserId", "ContactId", "RequestId", "Body", "RequestedAtUtc", "State", "Kind", "Origin")
                VALUES ({pending}, {actor}, {bot}, {request}, 'Fictitious pending reply', {now}, 'Pending', 'Text', 'Conversation');
                """, Ct);
            await db.Database.MigrateAsync(Ct);
            var changed = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == bot, Ct);
            Assert.Equal(WhatsAppConversationMode.Manual, changed.Mode);
            Assert.Equal(8, changed.ModeRevision);
            Assert.Equal(1, changed.LastInboundSequence);
            Assert.Equal(8, (await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == manual, Ct)).ModeRevision);
            Assert.Equal("Fictitious prior message", (await db.Set<WhatsAppMessage>().SingleAsync(item => item.Id == message, Ct)).Body);
            var outgoing = await db.Set<WhatsAppOutgoingMessage>().SingleAsync(item => item.Id == pending, Ct);
            Assert.Equal(WhatsAppOutgoingMessageState.Pending, outgoing.State);
            Assert.Equal(request, outgoing.RequestId);
            Assert.Equal(actor, outgoing.OperatorUserId);
            changed.ChangeMode(WhatsAppConversationMode.Assistant);
            await db.SaveChangesAsync(Ct);
            await db.GetService<IMigrator>().MigrateAsync(Before, Ct);
            db.ChangeTracker.Clear();
            var restored = await db.Database.SqlQuery<string>($"""SELECT "Mode" AS "Value" FROM "WhatsAppContacts" WHERE "Id" = {bot}""").SingleAsync(Ct);
            Assert.Equal("Manual", restored);
            var revision = await db.Database.SqlQuery<long>($"""SELECT "ModeRevision" AS "Value" FROM "WhatsAppContacts" WHERE "Id" = {bot}""").SingleAsync(Ct);
            Assert.Equal(10, revision);
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "WhatsAppMessages" WHERE "Id" = {message}""").SingleAsync(Ct));
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "WhatsAppOutgoingMessages" WHERE "Id" = {pending}""").SingleAsync(Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
