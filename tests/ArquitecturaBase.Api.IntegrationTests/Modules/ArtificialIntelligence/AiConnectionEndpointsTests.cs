using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiConnectionEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_permission_is_independent_and_receipts_belong_to_the_operator()
    {
        using var reader = await OperatorAsync([ArtificialIntelligencePermissions.Read]);
        using var read = await reader.GetAsync("/api/ai/connection", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var write = await reader.PutAsJsonAsync("/api/ai/connection", new { }, Ct);
        using var test = await reader.PostAsJsonAsync("/api/ai/connection/test", new { }, Ct);
        using var models = await reader.PostAsJsonAsync("/api/ai/connection/models", new { }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, test.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, models.StatusCode);
        using var manager = await OperatorAsync(ArtificialIntelligencePermissions.All);
        using var admin = await AdminAsync();
        var revision = (await ReadAsync(admin)).GetProperty("revision").GetInt64();
        var id = Guid.NewGuid();
        using var saved = await admin.PutAsJsonAsync("/api/ai/connection", Payload(revision, id, "fictional-private-key"), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var anotherOperator = await manager.GetAsync($"/api/ai/connection/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, anotherOperator.StatusCode);
        using var manageOnly = await OperatorAsync([ArtificialIntelligencePermissions.Manage]);
        using var missingRead = await manageOnly.GetAsync("/api/ai/connection", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, missingRead.StatusCode);
    }

    [Fact]
    public async Task The_test_rate_limit_is_partitioned_by_authenticated_operator()
    {
        using var firstOperator = await OperatorAsync(ArtificialIntelligencePermissions.All);
        using var secondOperator = await OperatorAsync(ArtificialIntelligencePermissions.All);
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:RateLimiting:TestPermitLimit", "1"));
        using var first = host.CreateClient();
        using var second = host.CreateClient();
        first.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, firstOperator.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, secondOperator.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var revision = (await ReadAsync(first)).GetProperty("revision").GetInt64();
        var body = new { provider = "OpenAI", model = "fictional-model", credentialChange = "Replace", apiKey = "fictional-key", expectedRevision = revision };
        using var allowed = await first.PostAsJsonAsync("/api/ai/connection/test", body, Ct);
        using var limited = await first.PostAsJsonAsync("/api/ai/connection/test", body, Ct);
        using var otherAllowed = await second.PostAsJsonAsync("/api/ai/connection/test", body, Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherAllowed.StatusCode);
        Assert.True((await limited.ReadJsonAsync()).TryGetProperty("retryAfter", out _));
    }

    private async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> permissions)
    {
        var id = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var name = "ai-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(name, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("ai-operator")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return client;
    }

    [Theory]
    [InlineData("GET", "/api/ai/connection")]
    [InlineData("PUT", "/api/ai/connection")]
    [InlineData("POST", "/api/ai/connection/test")]
    [InlineData("POST", "/api/ai/connection/models")]
    [InlineData("GET", "/api/ai/connection/changes/00000000-0000-0000-0000-000000000002")]
    public async Task Every_route_requires_a_session_and_module_permissions(string method, string path)
    {
        using var client = factory.CreateClient();
        using var anonymous = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = method == "GET" ? null : JsonContent.Create(new { }) }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("ai-no-permission"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var denied = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = method == "GET" ? null : JsonContent.Create(new { }) }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Listing_models_accepts_a_draft_without_a_model_and_never_saves_or_returns_the_key()
    {
        using var client = await AdminAsync();
        var before = await ReadAsync(client);
        var revision = before.GetProperty("revision").GetInt64();
        var payload = new { provider = "Anthropic", credentialChange = "Replace", apiKey = "fictional-catalog-key", expectedRevision = revision };
        using var response = await client.PostAsJsonAsync("/api/ai/connection/models", payload, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Anthropic", body.GetProperty("provider").GetString());
        Assert.Equal(revision, body.GetProperty("revision").GetInt64());
        Assert.Equal("fictional-model", Assert.Single(body.GetProperty("models").EnumerateArray()).GetProperty("id").GetString());
        Assert.DoesNotContain("fictional-catalog-key", body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var stale = await client.PostAsJsonAsync("/api/ai/connection/models", payload with { expectedRevision = revision + 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Ai.Connection.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        using var invalid = await client.PostAsJsonAsync("/api/ai/connection/models", payload with { apiKey = "fictional\r\nkey" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.ReadJsonAsync()).GetProperty("errors").TryGetProperty("apiKey", out _));
    }

    [Fact]
    public async Task Concurrent_hosts_serialize_saves_and_receipts_recover_an_identical_request()
    {
        using var client = await AdminAsync();
        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        using var second = secondHost.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var before = await ReadAsync(client);
        var revision = before.GetProperty("revision").GetInt64();
        var id = Guid.NewGuid();
        var payload = Payload(revision, id, "fictional-key-a");
        var responses = await Task.WhenAll(client.PutAsJsonAsync("/api/ai/connection", payload, Ct), second.PutAsJsonAsync("/api/ai/connection", payload, Ct));
        using var first = responses[0];
        using var retry = responses[1];
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await first.ReadJsonAsync()).GetRawText(), (await retry.ReadJsonAsync()).GetRawText());
        Assert.Equal(revision + 1, (await ReadAsync(second)).GetProperty("revision").GetInt64());
        using var reused = await second.PutAsJsonAsync("/api/ai/connection", Payload(revision, id, "fictional-key-b"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var receipt = await client.GetAsync($"/api/ai/connection/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var text = await receipt.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("fictional-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedApiKey", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Different_concurrent_edits_have_one_winner_and_one_revision_conflict()
    {
        using var client = await AdminAsync();
        await using var otherHost = factory.WithWebHostBuilder(_ => { });
        using var other = otherHost.CreateClient();
        other.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        var results = await Task.WhenAll(client.PutAsJsonAsync("/api/ai/connection", Payload(revision, Guid.NewGuid(), "fictional-a"), Ct),
            other.PutAsJsonAsync("/api/ai/connection", Payload(revision, Guid.NewGuid(), "fictional-b"), Ct));
        using var first = results[0];
        using var second = results[1];
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(x => x.StatusCode).Order());
        Assert.Equal(revision + 1, (await ReadAsync(other)).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task Failed_commit_rolls_back_the_connection_and_its_receipt_together()
    {
        using var client = await AdminAsync();
        var before = await ReadAsync(client);
        var id = Guid.NewGuid();
        var probe = new CommitFailureProbe();
        await using var failingHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => FailingCommitUnitOfWork.Replace(services, probe)));
        using var failing = failingHost.CreateClient();
        failing.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var response = await failing.PutAsJsonAsync("/api/ai/connection", Payload(before.GetProperty("revision").GetInt64(), id, "fictional-rollback"), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(probe.RolledBackBeforeLeaving);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var receipt = await client.GetAsync($"/api/ai/connection/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
    }

    [Fact]
    public async Task Credentials_are_encrypted_and_testing_does_not_change_the_connection()
    {
        using var client = await AdminAsync();
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        using var saved = await client.PutAsJsonAsync("/api/ai/connection", Payload(revision, Guid.NewGuid(), "fictional-private-key"), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<AiConnection>().SingleAsync(Ct));
        Assert.DoesNotContain("fictional-private-key", stored.ProtectedApiKey!, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, stored.CreatedBy);
        var before = await ReadAsync(client);
        using var tested = await client.PostAsJsonAsync("/api/ai/connection/test", new { provider = "OpenAI", model = "fictional-model", credentialChange = "Keep", expectedRevision = revision + 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var invalid = await client.PostAsJsonAsync("/api/ai/connection/test", new { provider = "Anthropic", model = "fictional-model", credentialChange = "Keep", expectedRevision = revision + 1 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Ai.Connection.ProviderChanged", (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Invalid_credentials_use_localized_field_errors_without_echoing_the_secret(string culture)
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        using var response = await client.PutAsJsonAsync("/api/ai/connection", Payload(revision, Guid.NewGuid(), "secret\r\nheader"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty("apiKey", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("secret", problem.GetRawText(), StringComparison.Ordinal);
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var me = await client.GetAsync("/api/me", Ct);
        var userId = (await me.ReadJsonAsync()).GetProperty("id").GetString();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }
    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/ai/connection", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
    private static object Payload(long revision, Guid id, string key) => new
        { provider = "OpenAI", model = "fictional-model", credentialChange = "Replace", apiKey = key, enabled = true, expectedRevision = revision, requestId = id };
}
