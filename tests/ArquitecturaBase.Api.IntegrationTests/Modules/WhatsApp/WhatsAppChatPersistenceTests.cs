using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
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
            await db.Database.MigrateAsync(Ct);
            var messages = await db.Set<WhatsAppMessage>().OrderBy(message => message.InboundSequence).ToListAsync(Ct);
            Assert.Equal([1L, 2L], messages.Select(message => message.InboundSequence));
            Assert.Equal(["Primero", "Segundo"], messages.Select(message => message.Body));
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
}
