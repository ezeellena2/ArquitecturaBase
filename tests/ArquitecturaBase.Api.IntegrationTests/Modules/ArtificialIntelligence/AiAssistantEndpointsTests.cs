using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task Public_presentation_advertises_the_installed_assistant_module()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/settings/presentation", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var presentation = await response.ReadJsonAsync();
        Assert.Contains("assistant", presentation.GetProperty("installedFeatures").EnumerateArray().Select(value => value.GetString()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/api/ai/assistant/bootstrap")]
    [InlineData("GET", "/api/ai/assistant/conversations")]
    [InlineData("GET", "/api/ai/assistant/conversations/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/ai/assistant/conversations/00000000-0000-0000-0000-000000000001/turns")]
    [InlineData("POST", "/api/ai/assistant/turns")]
    [InlineData("GET", "/api/ai/assistant/turns/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/ai/assistant/turns/00000000-0000-0000-0000-000000000001/cancel")]
    [InlineData("DELETE", "/api/ai/assistant/conversations/00000000-0000-0000-0000-000000000001")]
    public async Task Every_assistant_route_requires_session_and_its_independent_permission(string method, string path)
    {
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.SendAsync(new(new HttpMethod(method), path) { Content = method == "POST" ? JsonContent.Create(new { }) : null }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var reader = await OperatorAsync([ArtificialIntelligencePermissions.Read]);
        using var forbidden = await reader.SendAsync(new(new HttpMethod(method), path) { Content = method == "POST" ? JsonContent.Create(new { }) : null }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Lost_acceptance_is_recovered_with_the_same_UUID_without_starting_generation()
    {
        var revision = await ConfigureConnectionAsync();
        using var actor = await OperatorAsync([ArtificialIntelligencePermissions.AssistantUse]);
        var requestId = Guid.NewGuid();
        var payload = new { requestId, conversationId = (Guid?)null, expectedRevision = (long?)null, expectedConnectionRevision = revision, question = "Question with private context" };
        using var accepted = await actor.PostAsJsonAsync("/api/ai/assistant/turns", payload, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        using var repeated = await actor.PostAsJsonAsync("/api/ai/assistant/turns", payload, Ct);
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        var again = await repeated.ReadJsonAsync();
        Assert.Equal(receipt.GetProperty("conversationId").GetGuid(), again.GetProperty("conversationId").GetGuid());
        using var recovered = await actor.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("no-store", recovered.Headers.CacheControl?.NoStore == true ? "no-store" : "missing");
        var turn = await recovered.ReadJsonAsync();
        Assert.Equal("pending", turn.GetProperty("state").GetString());
        Assert.Equal(payload.question, turn.GetProperty("question").GetString());
        Assert.EndsWith("Z", turn.GetProperty("submittedAtUtc").GetString(), StringComparison.Ordinal);
        using var reused = await actor.PostAsJsonAsync("/api/ai/assistant/turns", payload with { question = "different" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("Ai.Assistant.RequestReused", (await reused.ReadJsonAsync()).GetProperty("code").GetString());
        using var other = await OperatorAsync([ArtificialIntelligencePermissions.AssistantUse]);
        using var hidden = await other.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var canceled = await actor.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        Assert.Equal("canceled", (await canceled.ReadJsonAsync()).GetProperty("state").GetString());
        using var removed = await actor.DeleteAsync($"/api/ai/assistant/conversations/{receipt.GetProperty("conversationId").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var afterRemoval = await actor.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, afterRemoval.StatusCode);
        var metadata = await afterRemoval.ReadJsonAsync();
        Assert.NotEqual("allowed", metadata.GetProperty("contentAccess").GetString());
        Assert.False(metadata.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task Invalid_or_extra_fields_are_rejected_and_language_comes_from_the_header()
    {
        var revision = await ConfigureConnectionAsync();
        using var actor = await OperatorAsync([ArtificialIntelligencePermissions.AssistantUse]);
        actor.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var invalid = await actor.PostAsJsonAsync("/api/ai/assistant/turns", new { requestId = Guid.NewGuid(), expectedConnectionRevision = revision, question = " " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var extra = await actor.PostAsJsonAsync("/api/ai/assistant/turns", new { requestId = Guid.NewGuid(), expectedConnectionRevision = revision, question = "help", ownerUserId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        var requestId = Guid.NewGuid();
        using var accepted = await actor.PostAsJsonAsync("/api/ai/assistant/turns", new { requestId, expectedConnectionRevision = revision, question = "help" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var recovered = await actor.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal("en", (await recovered.ReadJsonAsync()).GetProperty("culture").GetString());
        using var cancel = await actor.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
    }

    private async Task<long> ConfigureConnectionAsync()
    {
        using var manager = await OperatorAsync([ArtificialIntelligencePermissions.Read, ArtificialIntelligencePermissions.Manage]);
        using var current = await manager.GetAsync("/api/ai/connection", Ct);
        var revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
        using var configured = await manager.PutAsJsonAsync("/api/ai/connection", new
        {
            provider = "OpenAI", model = "fictional-model", credentialChange = "Replace", apiKey = "fictional-private-key",
            enabled = true, expectedRevision = revision, requestId = Guid.NewGuid(),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        return (await configured.ReadJsonAsync()).GetProperty("connection").GetProperty("revision").GetInt64();
    }

    private async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> permissions)
    {
        var id = await factory.InTransactionAsync(async services =>
        {
            var name = "assistant-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-operator")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return client;
    }
}
