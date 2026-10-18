using System.Reflection;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantRetentionQueryTests
{
    [Fact]
    public void Channel_retention_translates_with_active_channel_selection_and_live_receipt_protection()
    {
        var clock = new FakeTimeProvider();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var repository = new AiAssistantRepository(db, clock);
        var method = typeof(AiAssistantRepository).GetMethod("Removable", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<AiConversation>>(method.Invoke(repository, [clock.GetUtcNow().UtcDateTime]));
        var sql = query.Take(10).ToQueryString();
        Assert.Contains("ChannelId", sql, StringComparison.Ordinal);
        Assert.Contains("home", sql, StringComparison.Ordinal);
        Assert.Contains("AiAssistantRequestReceipts", sql, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ContentExpiresAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("Pending", sql, StringComparison.Ordinal);
        Assert.Contains("Running", sql, StringComparison.Ordinal);
    }
}
