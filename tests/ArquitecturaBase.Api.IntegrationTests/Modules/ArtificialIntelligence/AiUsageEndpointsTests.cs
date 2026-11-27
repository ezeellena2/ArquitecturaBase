using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

/// <summary>Consumo of the AI (PR12, PR13 and PR15): a month far in the past and models of its own keep it apart from other tests.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class AiUsageEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime MidMarch2025 = new(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("GET", "/api/ai/usage?month=2025-03")]
    [InlineData("PUT", "/api/ai/usage/prices")]
    public async Task Every_route_requires_a_session_and_the_module_permissions(string method, string path)
    {
        using var client = factory.CreateClient();
        using var anonymous = await client.SendAsync(Request(method, path), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("ai-usage-no-permission"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var denied = await client.SendAsync(Request(method, path), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Whoever_sees_the_configuration_sees_the_usage_and_only_whoever_manages_it_loads_prices()
    {
        using var reader = await OperatorAsync([ArtificialIntelligencePermissions.Read]);
        using var read = await reader.GetAsync("/api/ai/usage?month=2025-03", Ct);
        using var write = await reader.PutAsJsonAsync("/api/ai/usage/prices", Price(Model("denied"), 1m, 1m, null, null), Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Theory]
    [InlineData("03/2025")]
    [InlineData("9999-12")]
    [InlineData("")]
    public async Task A_month_that_is_not_a_usable_year_dash_month_is_rejected(string month)
    {
        using var client = await OperatorAsync(ArtificialIntelligencePermissions.All);
        using var response = await client.GetAsync($"/api/ai/usage?month={month}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("month", out _));
    }

    [Fact]
    public async Task The_month_adds_the_kept_usage_and_the_live_turns_that_reached_the_provider_with_their_estimated_cost()
    {
        var text = Model("text");
        var audio = Model("audio");
        await SeedAsync(
            Turn(text, MidMarch2025, 1_500_000, 60_000),
            Turn(text, MidMarch2025.AddDays(5), 500_000, 40_000),
            Turn(text, new DateTime(2025, 4, 15, 12, 0, 0, DateTimeKind.Utc), 9_000_000, 9_000),
            Turn(text, MidMarch2025, null, null, reachProvider: false),
            Turn(audio, MidMarch2025, null, null, audioSeconds: 90m));
        await factory.InTransactionAsync(services =>
        {
            var kept = AiUsageMonth.Create(new DateOnly(2025, 3, 1), AiProvider.OpenAI, text);
            kept.Add(3, 1, 1_000_000, null, null);
            services.GetRequiredService<ApplicationDbContext>().Add(kept);
            return Task.CompletedTask;
        });
        using var client = await OperatorAsync(ArtificialIntelligencePermissions.All);
        using var priced = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(text, 1.25m, 10m, null, null), Ct);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);

        using var response = await client.GetAsync("/api/ai/usage?month=2025-03", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("2025-03", body.GetProperty("month").GetString());
        var models = body.GetProperty("models").EnumerateArray().ToArray();
        var textRow = models.Single(row => row.GetProperty("model").GetString() == text);
        Assert.Equal("OpenAI", textRow.GetProperty("provider").GetString());
        Assert.Equal(5, textRow.GetProperty("requests").GetInt64());
        Assert.Equal(1, textRow.GetProperty("unknownUsageRequests").GetInt64());
        Assert.Equal(3_000_000, textRow.GetProperty("inputTokens").GetInt64());
        Assert.Equal(100_000, textRow.GetProperty("outputTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, textRow.GetProperty("audioSeconds").ValueKind);
        Assert.Equal(1, textRow.GetProperty("price").GetProperty("revision").GetInt64());
        // 3 × 1.25 + 0.1 × 10
        Assert.Equal(4.75m, textRow.GetProperty("estimatedCostUsd").GetDecimal());
        var audioRow = models.Single(row => row.GetProperty("model").GetString() == audio);
        Assert.Equal(JsonValueKind.Null, audioRow.GetProperty("inputTokens").ValueKind);
        Assert.Equal(90m, audioRow.GetProperty("audioSeconds").GetDecimal());
        Assert.Equal(JsonValueKind.Null, audioRow.GetProperty("price").ValueKind);
        Assert.Equal(JsonValueKind.Null, audioRow.GetProperty("estimatedCostUsd").ValueKind);
        Assert.True(body.GetProperty("unpricedModels").GetInt32() >= 1);
        Assert.True(body.GetProperty("totalEstimatedCostUsd").GetDecimal() >= 4.75m);
    }

    [Fact]
    public async Task Prices_are_loaded_once_and_then_changed_with_their_revision()
    {
        var model = Model("prices");
        using var client = await OperatorAsync(ArtificialIntelligencePermissions.All);

        using var created = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(model, 1.25m, 10m, null, null), Ct);
        using var createdAgain = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(model, 2m, 10m, null, null), Ct);
        using var updated = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(model, 2m, null, 0.006m, 1), Ct);
        using var stale = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(model, 3m, null, null, 1), Ct);
        using var invalid = await client.PutAsJsonAsync("/api/ai/usage/prices", Price(model, -1m, 1000.5m, null, 2), Ct);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(1, (await created.ReadJsonAsync()).GetProperty("revision").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, createdAgain.StatusCode);
        Assert.Equal("Ai.ModelPrice.RevisionConflict", (await createdAgain.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await updated.ReadJsonAsync();
        Assert.Equal(2, saved.GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("outputUsdPerMillion").ValueKind);
        Assert.Equal(0.006m, saved.GetProperty("audioUsdPerMinute").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("inputUsdPerMillion", out _));
        Assert.True(errors.TryGetProperty("outputUsdPerMillion", out _));
    }

    private static string Model(string kind) => $"fictional-{kind}-{Guid.NewGuid():N}"[..40];

    private static object Price(string model, decimal? input, decimal? output, decimal? audio, long? revision) => new
    {
        provider = "OpenAI", model, inputUsdPerMillion = input, outputUsdPerMillion = output, audioUsdPerMinute = audio, revision,
    };

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path) { Content = method == "GET" ? null : JsonContent.Create(new { }) };

    private static (AiConversation Conversation, AiAssistantTurn Turn) Turn(string model, DateTime submittedAtUtc, long? input, long? output,
        decimal? audioSeconds = null, bool reachProvider = true)
    {
        var conversation = AiConversation.Create(Guid.NewGuid(), submittedAtUtc);
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
            AiProvider.OpenAI, model, 1, submittedAtUtc, submittedAtUtc.AddDays(30));
        if (reachProvider)
        {
            Assert.True(turn.Claim(Guid.NewGuid(), submittedAtUtc.AddMinutes(2)).IsSuccess);
            Assert.True(turn.BeginProviderStep(turn.Fence, submittedAtUtc.AddSeconds(1), 5).IsSuccess);
            turn.RecordUsage(input, output, audioSeconds);
        }
        return (conversation, turn);
    }

    private Task SeedAsync(params (AiConversation Conversation, AiAssistantTurn Turn)[] rows) =>
        factory.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            foreach (var (conversation, turn) in rows) db.AddRange(conversation, turn);
            return Task.CompletedTask;
        });

    private async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> permissions)
    {
        var id = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var name = "ai-usage-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(name, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("ai-usage-operator")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return client;
    }
}
