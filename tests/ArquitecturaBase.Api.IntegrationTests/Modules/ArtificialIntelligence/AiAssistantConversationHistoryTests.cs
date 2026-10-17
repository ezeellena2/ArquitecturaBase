using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantConversationHistoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_and_detail_derive_the_same_private_title_and_hide_other_owners()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var accepted = await scenario.SubmitAsync(Guid.NewGuid(), "¿Qué roles\n  tenemos?");
        var id = accepted.GetProperty("conversationId").GetGuid();
        using var list = await scenario.Client.GetAsync("/api/ai/assistant/conversations?page=1&pageSize=20&sort=-lastActivityAtUtc", Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var row = Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("¿Qué roles tenemos?", row.GetProperty("title").GetString());
        using var detail = await scenario.Client.GetAsync($"/api/ai/assistant/conversations/{id}", Ct);
        Assert.Equal(row.GetProperty("title").GetString(), (await detail.ReadJsonAsync()).GetProperty("title").GetString());
        Assert.True(list.Headers.CacheControl?.NoStore);
        using var other = await AiAssistantTestScenario.CreateAsync(factory, configureConnection: false);
        using var hidden = await other.Client.GetAsync($"/api/ai/assistant/conversations/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var otherList = await other.Client.GetAsync("/api/ai/assistant/conversations", Ct);
        Assert.Empty((await otherList.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        using var removed = await scenario.Client.DeleteAsync($"/api/ai/assistant/conversations/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var after = await scenario.Client.GetAsync("/api/ai/assistant/conversations", Ct);
        Assert.Empty((await after.ReadJsonAsync()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Corrupted_content_fails_but_blocked_and_expired_titles_do_not_attempt_decryption()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var requestId = Guid.NewGuid();
        var accepted = await scenario.SubmitAsync(requestId, "Private original title");
        var id = accepted.GetProperty("conversationId").GetGuid();
        using var canceled = await scenario.Client.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
        await factory.InTransactionAsync(async services => {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var turn = await db.Set<AiAssistantTurn>().SingleAsync(turn => turn.ConversationId == id, Ct);
            turn.ProtectQuestion("invalid-ciphertext");
        });
        using var corrupt = await scenario.Client.GetAsync("/api/ai/assistant/conversations", Ct);
        Assert.False(corrupt.IsSuccessStatusCode);
        Assert.Equal("Ai.Assistant.ContentUnavailable", (await corrupt.ReadJsonAsync()).GetProperty("code").GetString());
        await factory.InTransactionAsync(async services => {
            var conversation = await services.GetRequiredService<ApplicationDbContext>().Set<AiConversation>().SingleAsync(row => row.Id == id, Ct);
            conversation.RequirePermissions(["source.permission.revoked"]);
        });
        using var blocked = await scenario.Client.GetAsync($"/api/ai/assistant/conversations/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        var blockedRow = await blocked.ReadJsonAsync();
        Assert.Equal("blocked", blockedRow.GetProperty("contentAccess").GetString());
        Assert.Equal("Conversación privada", blockedRow.GetProperty("title").GetString());

        // Expiry is checked before decrypting, even if the key/payload is unavailable.
        {
            // Read the projection with its original owner, ignoring the blocked fixture only for this title policy.
            var result = await scenario.ScopeAsync(async services => {
                var reader = services.GetRequiredService<ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence.IAiAssistantReader>();
                return await reader.FindConversationAsync(scenario.OwnerUserId, id, Ct);
            });
            Assert.NotNull(result);
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(result.FirstQuestion!.ContentExpiresAtUtc, TimeSpan.Zero));
            var title = await scenario.ScopeAsync(services => Task.FromResult(new ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantResponsePolicy(
                services.GetRequiredService<ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations.IAiConversationProtector>(), clock, services.GetRequiredService<ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantCapabilityCatalog>()).MapTitle(result, true)));
            Assert.Equal("Conversación privada", title.Value);
        }
    }

    [Fact]
    public async Task A_page_projects_only_one_question_per_row_with_two_SQL_commands_and_no_payload_N_plus_one()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var conversationIds = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var requestId = Guid.NewGuid();
            var accepted = await scenario.SubmitAsync(requestId, $"Question {index}");
            conversationIds.Add(accepted.GetProperty("conversationId").GetGuid());
            using var canceled = await scenario.Client.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
            Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        }
        var recorder = new AssistantReadCommandRecorder();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
        var reader = new AiAssistantReader(db, factory.Clock);
        var page = await reader.ListConversationsAsync(scenario.OwnerUserId, new ListAiConversationsRequest { Page = 1, PageSize = 2, Sort = "-lastActivityAtUtc" }, Ct);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        // Activity is tied with the fake clock; ApplySort uses an ascending unique tie-breaker.
        Assert.Equal(conversationIds.Order().Take(2), page.Items.Select(row => row.Id));
        Assert.All(page.Items, row => Assert.NotNull(row.FirstQuestion?.ProtectedQuestion));
        Assert.Equal(2, recorder.Commands.Count);
        Assert.All(recorder.Commands, sql => {
            Assert.DoesNotContain("ProtectedAnswer", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ProtectedEvidences", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("AiAssistantToolExecutions", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        });
    }
}
