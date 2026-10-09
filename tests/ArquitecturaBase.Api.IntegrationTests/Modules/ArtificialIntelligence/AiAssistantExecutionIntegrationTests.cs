using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantExecutionIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AiProvider.OpenAI, "users.summary")]
    [InlineData(AiProvider.Anthropic, "users.summary")]
    [InlineData(AiProvider.OpenAI, "roles.list")]
    [InlineData(AiProvider.Anthropic, "roles.list")]
    [InlineData(AiProvider.OpenAI, "roles.permissions")]
    [InlineData(AiProvider.Anthropic, "roles.permissions")]
    [InlineData(AiProvider.OpenAI, "business.information")]
    [InlineData(AiProvider.Anthropic, "business.information")]
    [InlineData(AiProvider.OpenAI, "system.help")]
    [InlineData(AiProvider.Anthropic, "system.help")]
    public async Task Both_http_adapters_publish_verified_postgres_sources_and_requery_on_followup(AiProvider provider, string toolId)
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, provider, toolId);
        using var observedFlow = scenario.Transactions.ObserveFlow();
        var culture = provider == AiProvider.OpenAI ? "es" : "en";
        scenario.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var requestId = Guid.NewGuid();
        var accepted = await scenario.SubmitAsync(requestId, "First question for " + toolId);
        Assert.Empty(scenario.Handler.Requests);
        Assert.True(await scenario.ProcessNextAsync());
        var first = await ReadTurnAsync(scenario, requestId);
        Assert.Equal("completed", first.GetProperty("state").GetString());
        Assert.Equal("allowed", first.GetProperty("contentAccess").GetString());
        Assert.Equal(culture, first.GetProperty("culture").GetString());
        Assert.Equal("America/New_York", first.GetProperty("timeZoneId").GetString());
        var evidence = Assert.Single(first.GetProperty("evidences").EnumerateArray());
        Assert.Equal(toolId, evidence.GetProperty("sourceId").GetString());
        Assert.True(evidence.GetProperty("isComplete").GetBoolean());
        Assert.EndsWith("Z", evidence.GetProperty("observedAtUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(evidence.GetProperty("id").GetGuid(), Assert.Single(first.GetProperty("answer").GetProperty("evidenceIds").EnumerateArray()).GetGuid());
        await AssertSourceAsync(scenario, toolId, culture, evidence);
        Assert.Equal(2, scenario.Handler.Requests.Count);
        var conversationId = accepted.GetProperty("conversationId").GetGuid();
        using var conversation = await scenario.Client.GetAsync($"/api/ai/assistant/conversations/{conversationId}", Ct);
        Assert.Equal(HttpStatusCode.OK, conversation.StatusCode);
        var revision = (await conversation.ReadJsonAsync()).GetProperty("revision").GetInt64();
        var followupId = Guid.NewGuid();
        await scenario.SubmitAsync(followupId, "Followup question for " + toolId, conversationId, revision);
        Assert.True(await scenario.ProcessNextAsync());
        var followup = await ReadTurnAsync(scenario, followupId);
        Assert.Equal("completed", followup.GetProperty("state").GetString());
        Assert.Equal(2, followup.GetProperty("ordinal").GetInt32());
        var fresh = Assert.Single(followup.GetProperty("evidences").EnumerateArray());
        Assert.NotEqual(evidence.GetProperty("id").GetGuid(), fresh.GetProperty("id").GetGuid());
        await AssertSourceAsync(scenario, toolId, culture, fresh);
        Assert.Equal(4, scenario.Handler.Requests.Count);
        var initialFollowup = string.Join(" | ", scenario.Handler.Requests[2].GetProperty(provider == AiProvider.OpenAI ? "input" : "messages")
            .EnumerateArray().Select(message => message.GetProperty("content").GetString()));
        Assert.Contains("First question for " + toolId, initialFollowup, StringComparison.Ordinal);
        Assert.Contains(first.GetProperty("answer").GetProperty("text").GetString()!, initialFollowup, StringComparison.Ordinal);
        Assert.All(scenario.Handler.ActiveTransactionsDuringHttp, active => Assert.Equal(0, active));
        Assert.True(scenario.Transactions.StartedTransactions >= 10);
        Assert.Equal(0, scenario.Transactions.ActiveTransactions);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var turns = await db.Set<AiAssistantTurn>().OrderBy(turn => turn.Ordinal).ToListAsync(Ct);
            Assert.Equal(2, turns.Count);
            Assert.All(turns, turn =>
            {
                Assert.Equal(AiAssistantTurnState.Completed, turn.State);
                Assert.Equal(2, turn.ProviderSteps);
                Assert.Equal(1, turn.ToolCalls);
                Assert.NotNull(turn.ProtectedQuestion);
                Assert.DoesNotContain("question", turn.ProtectedQuestion, StringComparison.Ordinal);
                Assert.NotNull(turn.ProtectedAnswer);
                Assert.Equal(20, turn.InputTokens);
                Assert.Equal(4, turn.OutputTokens);
                Assert.False(turn.UsageUnknown);
            });
            Assert.Equal(2, await db.Set<AiAssistantToolExecution>().CountAsync(Ct));
            Assert.Equal(2, await db.Set<AiAssistantRequestReceipt>().CountAsync(Ct));
            return true;
        });
        Assert.False(await scenario.ProcessNextAsync());
        using var reload = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        var repeated = await scenario.SubmitAsync(requestId, "First question for " + toolId);
        Assert.Equal(conversationId, repeated.GetProperty("conversationId").GetGuid());
        Assert.False(await scenario.ProcessNextAsync());
        Assert.Equal(4, scenario.Handler.Requests.Count);
    }

    private static async Task<JsonElement> ReadTurnAsync(AiAssistantExecutionScenario scenario, Guid requestId)
    {
        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task AssertSourceAsync(AiAssistantExecutionScenario scenario, string toolId, string culture, JsonElement evidence)
    {
        var rows = evidence.GetProperty("rows").EnumerateArray().ToArray();
        if (toolId == "users.summary")
        {
            var counts = await scenario.ScopeAsync(async services =>
            {
                var users = services.GetRequiredService<ApplicationDbContext>().Users;
                return (Active: await users.CountAsync(user => user.IsActive, Ct), Inactive: await users.CountAsync(user => !user.IsActive, Ct));
            });
            var values = Assert.Single(rows).GetProperty("values").EnumerateArray().ToArray();
            Assert.Equal(SystemHelpTexts.Get("ActiveAccounts", culture), values[0].GetProperty("label").GetString());
            Assert.Equal(counts.Active.ToString(System.Globalization.CultureInfo.InvariantCulture), values[0].GetProperty("value").GetString());
            Assert.Equal(counts.Inactive.ToString(System.Globalization.CultureInfo.InvariantCulture), values[1].GetProperty("value").GetString());
        }
        else if (toolId is "roles.list" or "roles.permissions")
        {
            var role = Assert.Single(rows, row => row.GetProperty("id").GetGuid() == scenario.SupportRoleId);
            Assert.Equal("Support", role.GetProperty("values")[0].GetProperty("value").GetString());
            Assert.Equal("Confirmed support role", role.GetProperty("values")[1].GetProperty("value").GetString());
            Assert.Equal("1", role.GetProperty("values")[2].GetProperty("value").GetString());
            if (toolId == "roles.permissions")
            {
                Assert.Single(rows);
                Assert.Equal(Permissions.Users.Read, role.GetProperty("values")[3].GetProperty("label").GetString());
                Assert.Equal(culture == "es" ? "Ver usuarios" : "View users", role.GetProperty("values")[3].GetProperty("value").GetString());
                Assert.Equal(4, role.GetProperty("values").GetArrayLength());
            }
            else Assert.Equal(await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Roles.CountAsync(Ct)), rows.Length);
        }
        else if (toolId == "business.information")
        {
            Assert.Equal(scenario.BusinessRevision, evidence.GetProperty("revision").GetInt64());
            var values = Assert.Single(rows).GetProperty("values");
            Assert.Equal("Integration clinic", values[0].GetProperty("value").GetString());
            Assert.Equal("09:00–17:00", values[2].GetProperty("value").GetString());
            Assert.Equal("Example street 12", values[3].GetProperty("value").GetString());
            Assert.Equal("Confirmed consultation", values[4].GetProperty("value").GetString());
        }
        else
        {
            Assert.Equal("profile", evidence.GetProperty("destination").GetString());
            Assert.Equal(SystemHelpTexts.Get("ProfileHelp", culture), Assert.Single(rows).GetProperty("values")[0].GetProperty("value").GetString());
        }
    }
}
