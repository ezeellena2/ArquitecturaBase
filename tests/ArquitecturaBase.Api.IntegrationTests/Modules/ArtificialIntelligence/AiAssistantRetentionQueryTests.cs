using System.Reflection;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
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
        var now = clock.GetUtcNow().UtcDateTime;
        var query = Assert.IsAssignableFrom<IQueryable<AiConversation>>(method.Invoke(repository, [now, null]));
        var sql = query.Take(10).ToQueryString();
        Assert.Contains("ChannelId", sql, StringComparison.Ordinal);
        Assert.Contains("home", sql, StringComparison.Ordinal);
        Assert.Contains("AiAssistantRequestReceipts", sql, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ContentExpiresAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("Pending", sql, StringComparison.Ordinal);
        Assert.Contains("Running", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmittedAtUtc", sql, StringComparison.Ordinal);
        // RC1-A: with the Home days shortened, a Home turn accepted before the cut no longer holds its conversation.
        var shortened = Assert.IsAssignableFrom<IQueryable<AiConversation>>(method.Invoke(repository,
            [now, new AiAssistantHomeRetention(now.AddDays(-7), ["home", "home-audio", "home-document"])]));
        Assert.Contains("SubmittedAtUtc", shortened.Take(10).ToQueryString(), StringComparison.Ordinal);
    }
}
