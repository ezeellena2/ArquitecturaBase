using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Durable_workload_counts_confirmed_acceptances_once_even_after_content_removal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var before = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantReader>().FindWorkloadAsync(nowUtc, 15, ct));
        var requestId = Guid.NewGuid();
        var accepted = await scenario.SubmitAsync(requestId);
        await scenario.SubmitAsync(requestId);
        var after = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantReader>().FindWorkloadAsync(nowUtc, 15, ct));
        Assert.Equal(before.AcceptedToday + 1, after.AcceptedToday);
        Assert.Equal(before.Pending + 1, after.Pending);
        Assert.NotNull(after.OldestPendingAtUtc);
        using var removed = await scenario.Client.DeleteAsync($"/api/ai/assistant/conversations/{accepted.GetProperty("conversationId").GetGuid()}", ct);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, removed.StatusCode);
        var retained = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantReader>().FindWorkloadAsync(nowUtc, 15, ct));
        Assert.Equal(after.AcceptedToday, retained.AcceptedToday);
        Assert.Equal(before.Pending, retained.Pending);
        Assert.NotNull(retained.OldestOverdueContentAtUtc);
    }

    [Fact]
    public async Task Durable_operations_have_database_constraints_for_owner_deduplication_and_fencing()
    {
        await factory.ExecuteDbContextAsync(db =>
        {
            var conversation = Entity(db.Model, "AiConversation");
            var turn = Entity(db.Model, "AiAssistantTurn");
            var receipt = Entity(db.Model, "AiAssistantRequestReceipt");
            var tool = Entity(db.Model, "AiAssistantToolExecution");
            Assert.NotEmpty(conversation.GetDeclaredQueryFilters());
            Assert.NotEmpty(turn.GetDeclaredQueryFilters());
            Assert.True(conversation.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.True(turn.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.True(turn.FindProperty("Fence")!.IsConcurrencyToken);
            Assert.Contains(receipt.GetIndexes(), index => index.IsUnique && Names(index, "OwnerUserId", "RequestId"));
            Assert.Contains(turn.GetIndexes(), index => index.IsUnique && Names(index, "ConversationId", "Ordinal"));
            Assert.Contains(turn.GetIndexes(), index => index.IsUnique && Names(index, "ConversationId") && index.GetFilter() is not null);
            Assert.Contains(turn.GetIndexes(), index => index.IsUnique && Names(index, "OwnerUserId", "ChannelId")
                && index.GetFilter() is { } filter && filter.Contains("home", StringComparison.Ordinal));
            Assert.DoesNotContain(turn.GetIndexes(), index => index.IsUnique && Names(index, "OwnerUserId"));
            Assert.Contains(tool.GetIndexes(), index => index.IsUnique && Names(index, "TurnId", "Ordinal"));
            Assert.Contains(turn.GetForeignKeys(), key => key.PrincipalEntityType == conversation && key.Properties.Count == 2);
            return Task.CompletedTask;
        });
    }

    private static IEntityType Entity(IModel model, string name) =>
        Assert.Single(model.GetEntityTypes(), entity => entity.ClrType.Name == name);

    private static bool Names(IIndex index, params string[] names) =>
        index.Properties.Select(property => property.Name).SequenceEqual(names, StringComparer.Ordinal);
}
