using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
using Microsoft.Extensions.Time.Testing;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantConversationScopeQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_reservation_query_matches_the_index_for_an_explicit_or_missing_channel_conversation(bool scoped)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var repository = new AiAssistantRepository(db, new FakeTimeProvider());
        var query = Assert.IsAssignableFrom<IQueryable<AiAssistantTurn>>(typeof(AiAssistantRepository)
            .GetMethod("ActiveForActorChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(repository, [Guid.NewGuid(), "fixture-manual", scoped ? Guid.NewGuid() : null]));
        var sql = query.ToQueryString();
        var where = sql[sql.LastIndexOf("WHERE ", StringComparison.Ordinal)..];
        Assert.Contains(scoped ? "\"ChannelConversationId\" =" : "\"ChannelConversationId\" IS NULL", where, StringComparison.Ordinal);
        Assert.Contains("\"OwnerUserId\" =", where, StringComparison.Ordinal);
        Assert.Contains("\"ChannelId\" =", where, StringComparison.Ordinal);
        Assert.Contains("Pending", where, StringComparison.Ordinal);
        Assert.Contains("Running", where, StringComparison.Ordinal);
        Assert.Contains("IS DISTINCT FROM '1'::jsonb", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_interpretations_are_unique_by_actor_channel_and_nullable_channel_conversation()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AiAssistantTurn))!;
        var index = entity.GetIndexes().SingleOrDefault(candidate => candidate.Properties.Select(property => property.Name)
            .SequenceEqual(["OwnerUserId", "ChannelId", "ChannelConversationId"]));
        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.False(index.GetAreNullsDistinct());
        var filter = index.GetFilter()!;
        Assert.Contains("home", filter, StringComparison.Ordinal);
        Assert.Contains("Pending", filter, StringComparison.Ordinal);
        Assert.Contains("Running", filter, StringComparison.Ordinal);
        Assert.Contains("IS JSON OBJECT", filter, StringComparison.Ordinal);
        Assert.Contains("IS DISTINCT FROM '1'::jsonb", filter, StringComparison.Ordinal);
    }
}
