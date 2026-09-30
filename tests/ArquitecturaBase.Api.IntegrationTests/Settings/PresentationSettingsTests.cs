using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

[Collection(ApiTestGroup.Name)]
public sealed class PresentationSettingsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Public_presentation_contains_only_the_three_defaults()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/settings/presentation", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.ReadJsonAsync();
        Assert.Equal(["defaultCulture", "defaultPageSize", "defaultTimeZoneId"],
            data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Patch_requires_authentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/settings", new { expectedRevision = 1, defaultPageSize = 50 }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_edits_have_one_winner_and_another_host_observes_the_commit()
    {
        await using var otherHost = factory.WithWebHostBuilder(_ => { });
        using var other = otherHost.CreateClient();
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var before = await ReadAsync(client, tokens.AccessToken);
        using var warmed = await other.GetAsync("/api/settings/presentation", Ct);
        Assert.Equal(HttpStatusCode.OK, warmed.StatusCode);
        try
        {
            var revision = before.GetProperty("revision").GetInt64();
            var responses = await Task.WhenAll(
                PatchAsync(client, tokens.AccessToken, new { expectedRevision = revision, defaultPageSize = 50 }),
                PatchAsync(client, tokens.AccessToken, new { expectedRevision = revision, defaultPageSize = 100 }));
            try
            {
                Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
                Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            }
            finally { foreach (var response in responses) response.Dispose(); }
            var current = await ReadAsync(client, tokens.AccessToken);
            using var observed = await other.GetAsync("/api/settings/presentation", Ct);
            Assert.Equal(current.GetProperty("defaultPageSize").GetInt32(),
                (await observed.ReadJsonAsync()).GetProperty("defaultPageSize").GetInt32());
        }
        finally
        {
            var current = await ReadAsync(client, tokens.AccessToken);
            using var restored = await PatchAsync(client, tokens.AccessToken,
                new { expectedRevision = current.GetProperty("revision").GetInt64(), defaultPageSize = before.GetProperty("defaultPageSize").GetInt32() });
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        }
    }

    [Theory]
    [InlineData("{\"expectedRevision\":1,\"defaultCulture\":null}")]
    [InlineData("{\"expectedRevision\":1,\"defaultPageSize\":25}")]
    [InlineData("{\"expectedRevision\":1,\"unknown\":true}")]
    [InlineData("{\"expectedRevision\":1}")]
    [InlineData("{\"expectedRevision\":1,\"defaultCulture\":\"en\",\"defaultPageSize\":50}")]
    public async Task Patch_rejects_invalid_or_ambiguous_changes(string json)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/settings")
        {
            Content = JsonContent.Create(JsonSerializer.Deserialize<JsonElement>(json)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_single_topic_is_persisted_and_a_stale_edit_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var before = await ReadAsync(client, tokens.AccessToken);
        try
        {
            var revision = before.GetProperty("revision").GetInt64();
            using var saved = await PatchAsync(client, tokens.AccessToken, new { expectedRevision = revision, defaultPageSize = 50 });
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            var current = await ReadAsync(client, tokens.AccessToken);
            Assert.Equal(50, current.GetProperty("defaultPageSize").GetInt32());
            Assert.Equal(before.GetProperty("defaultCulture").GetString(), current.GetProperty("defaultCulture").GetString());
            Assert.Equal(revision + 1, current.GetProperty("revision").GetInt64());
            using var stale = await PatchAsync(client, tokens.AccessToken, new { expectedRevision = revision, defaultCulture = "en" });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var problem = await stale.ReadJsonAsync();
            Assert.Equal("Settings.System.RevisionConflict", problem.GetProperty("code").GetString());
            using var publicResponse = await client.GetAsync("/api/settings/presentation", Ct);
            Assert.Equal(50, (await publicResponse.ReadJsonAsync()).GetProperty("defaultPageSize").GetInt32());
            using var users = await client.GetWithTokenAsync("/api/users", tokens.AccessToken);
            Assert.Equal(50, (await users.ReadJsonAsync()).GetProperty("pageSize").GetInt32());
            using var roles = await client.GetWithTokenAsync("/api/roles/paged", tokens.AccessToken);
            Assert.Equal(50, (await roles.ReadJsonAsync()).GetProperty("pageSize").GetInt32());
            using var explicitSize = await client.GetWithTokenAsync("/api/users?pageSize=10", tokens.AccessToken);
            Assert.Equal(10, (await explicitSize.ReadJsonAsync()).GetProperty("pageSize").GetInt32());
        }
        finally
        {
            var current = await ReadAsync(client, tokens.AccessToken);
            using var restored = await PatchAsync(client, tokens.AccessToken,
                new { expectedRevision = current.GetProperty("revision").GetInt64(), defaultPageSize = before.GetProperty("defaultPageSize").GetInt32() });
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string token)
    {
        using var response = await client.GetWithTokenAsync("/api/settings", token);
        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task Defaults_apply_to_new_accounts_and_missing_language_without_changing_existing_profiles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var before = await ReadAsync(client, tokens.AccessToken);
        using var profileResponse = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profileBefore = await profileResponse.ReadJsonAsync();
        try
        {
            using var cultureSave = await PatchAsync(client, tokens.AccessToken,
                new { expectedRevision = before.GetProperty("revision").GetInt64(), defaultCulture = "en" });
            Assert.Equal(HttpStatusCode.NoContent, cultureSave.StatusCode);
            var current = await ReadAsync(client, tokens.AccessToken);
            using var zoneSave = await PatchAsync(client, tokens.AccessToken,
                new { expectedRevision = current.GetProperty("revision").GetInt64(), defaultTimeZoneId = "Europe/Madrid" });
            Assert.Equal(HttpStatusCode.NoContent, zoneSave.StatusCode);

            using var fallback = await client.GetAsync("/api/settings/presentation", Ct);
            Assert.Equal(["en"], fallback.Content.Headers.ContentLanguage);
            using var explicitLanguage = new HttpRequestMessage(HttpMethod.Get, "/api/settings/presentation");
            explicitLanguage.Headers.AcceptLanguage.ParseAdd("fr;q=1,es-AR;q=0.8,en;q=0.5");
            using var explicitResponse = await client.SendAsync(explicitLanguage, Ct);
            Assert.Equal(["es"], explicitResponse.Content.Headers.ContentLanguage);

            using var created = await client.SendWithTokenAsync(HttpMethod.Post, "/api/users", tokens.AccessToken,
                new { email = TestEmails.Unique("default-preferences"), displayName = "Ana" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());
            var preferences = await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Id == id)
                .Select(user => new { user.Culture, user.TimeZoneId }).SingleAsync(Ct));
            Assert.Equal("en", preferences.Culture);
            Assert.Equal("Europe/Madrid", preferences.TimeZoneId);
            using var existing = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
            var profileAfter = await existing.ReadJsonAsync();
            Assert.Equal(profileBefore.GetProperty("culture").GetString(), profileAfter.GetProperty("culture").GetString());
            Assert.Equal(profileBefore.GetProperty("timeZoneId").GetString(), profileAfter.GetProperty("timeZoneId").GetString());
        }
        finally
        {
            foreach (var field in new[] { "defaultCulture", "defaultTimeZoneId" })
            {
                var current = await ReadAsync(client, tokens.AccessToken);
                using var restored = await PatchAsync(client, tokens.AccessToken,
                    new Dictionary<string, object?> { ["expectedRevision"] = current.GetProperty("revision").GetInt64(), [field] = before.GetProperty(field).GetString() });
                Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
            }
        }
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/settings") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }
}
