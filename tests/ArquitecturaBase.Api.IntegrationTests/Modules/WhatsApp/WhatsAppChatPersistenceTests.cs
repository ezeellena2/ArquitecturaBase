using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppChatPersistenceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_sequences_existing_inbound_messages_without_erasing_their_text()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("chat_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261001224951_SystemAppearance", Ct);
            var contactId = Guid.NewGuid();
            var earlier = factory.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
            var later = earlier.AddMinutes(1);
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppContacts" ("Id", "WaId", "ProfileName", "LastInboundAtUtc")
                VALUES ({contactId}, '5493515550000', 'Cliente anterior', {later});
                INSERT INTO "WhatsAppMessages" ("Id", "ContactId", "Direction", "WaMessageId", "Kind", "Body", "OccurredAtUtc")
                VALUES ({firstId}, {contactId}, 'Inbound', 'wamid.older', 'Text', 'Primero', {earlier}),
                       ({secondId}, {contactId}, 'Inbound', 'wamid.newer', 'Text', 'Segundo', {later});
                """, Ct);
            // The sequencing keeps the text; H16 empties it afterwards, when what a message says goes encrypted.
            await db.GetService<IMigrator>().MigrateAsync(WhatsAppChatContentMigration.BeforeMessageEncryption(db), Ct);
            Assert.Equal("Primero|Segundo", await db.Database.SqlQuery<string>(
                $"""SELECT string_agg("Body", '|' ORDER BY "InboundSequence") AS "Value" FROM "WhatsAppMessages" WHERE "ContactId" = {contactId}""").SingleAsync(Ct));
            await db.Database.MigrateAsync(Ct);
            var messages = await db.Set<WhatsAppMessage>().OrderBy(message => message.InboundSequence).ToListAsync(Ct);
            Assert.Equal([1L, 2L], messages.Select(message => message.InboundSequence));
            Assert.All(messages, message => Assert.Null(message.Body));
            var contact = await db.Set<WhatsAppContact>().SingleAsync(Ct);
            Assert.Equal(2, contact.LastInboundSequence);
            Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }

    [Fact]
    public async Task Independent_hosts_cannot_renew_or_release_another_dispatch_lease()
    {
        await using var second = factory.WithWebHostBuilder(_ => { });
        var firstCache = factory.Services.GetRequiredService<RedisCache>();
        var secondCache = second.Services.GetRequiredService<RedisCache>();
        var key = new RedisCacheKey("dispatch-test:" + Guid.NewGuid().ToString("N"));
        Assert.True(await firstCache.TryAcquireExclusiveLeaseAsync(key, "first", TimeSpan.FromSeconds(30), Ct));
        Assert.False(await secondCache.TryAcquireExclusiveLeaseAsync(key, "second", TimeSpan.FromSeconds(30), Ct));
        Assert.False(await secondCache.RenewExclusiveLeaseAsync(key, "second", TimeSpan.FromSeconds(30), Ct));
        await secondCache.ReleaseExclusiveLeaseAsync(key, "second", TimeSpan.Zero, Ct);
        Assert.False(await secondCache.TryAcquireExclusiveLeaseAsync(key, "second", TimeSpan.FromSeconds(30), Ct));
        Assert.True(await firstCache.RenewExclusiveLeaseAsync(key, "first", TimeSpan.FromSeconds(30), Ct));
        await firstCache.ReleaseExclusiveLeaseAsync(key, "first", TimeSpan.Zero, Ct);
        Assert.True(await secondCache.TryAcquireExclusiveLeaseAsync(key, "second", TimeSpan.FromSeconds(30), Ct));
        await firstCache.ReleaseExclusiveLeaseAsync(key, "first", TimeSpan.Zero, Ct);
        Assert.True(await secondCache.RenewExclusiveLeaseAsync(key, "second", TimeSpan.FromSeconds(30), Ct));
        await secondCache.ReleaseExclusiveLeaseAsync(key, "second", TimeSpan.Zero, Ct);
    }

    // A row lock gives back the contact the scope already read without refreshing it (EF): one read before the lock and unchanged since is
    // reloaded, so a conversation taken in between is seen; one this unit of work already changed keeps its change (O49 review).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_lock_of_a_contact_read_before_reloads_it_unless_this_unit_of_work_changed_it(bool changed)
    {
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var created = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid(), "Persona", now);
        created.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
        await InTransactionAsync(services => { services.GetRequiredService<ApplicationDbContext>().Add(created); return Task.CompletedTask; });

        WhatsAppContact? locked = null;
        await InTransactionAsync(async services =>
        {
            var read = (await services.GetRequiredService<IWhatsAppContactRepository>().GetByUserIdentifierAsync(created.UserIdentifier!, Ct))!;
            if (changed) read.RecordInbound(read.WaId, read.UserIdentifier, "Otro nombre", now);
            // Someone takes the conversation and saves before this unit of work takes the row.
            await InTransactionAsync(async other => (await other.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>()
                .SingleAsync(item => item.Id == created.Id, Ct)).ChangeMode(WhatsAppConversationMode.Manual, null, DateTime.UnixEpoch));
            locked = await services.GetRequiredService<IWhatsAppConversationRepository>().GetForUpdateAsync(created.Id, Ct);
            Assert.Same(read, locked);
        });

        Assert.Equal(changed ? (WhatsAppConversationMode.Assistant, "Otro nombre") : (WhatsAppConversationMode.Manual, "Persona"),
            (locked!.Mode, locked.ProfileName));
    }

    private async Task InTransactionAsync(Func<IServiceProvider, Task> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            await work(scope.ServiceProvider);
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
    }
}
