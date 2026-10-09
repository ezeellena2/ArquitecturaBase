using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiBusinessInformationEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Selected_place_persists_across_hosts_and_deduplicates_without_storing_provider_labels()
    {
        using var client = await AdminAsync();
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        var request = GuidedPayload(revision, Guid.NewGuid());
        request = request with { Details = request.Details! with { Location = request.Details!.Location with { Latitude = null, Longitude = null, PlaceId = "ChIJ_fictional" } } };
        using var saved = await client.PutAsJsonAsync("/api/ai/business-information", request, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        using var second = secondHost.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var location = (await ReadAsync(second)).GetProperty("details").GetProperty("location");
        Assert.Equal("ChIJ_fictional", location.GetProperty("placeId").GetString());
        Assert.Contains("query_place_id=ChIJ_fictional", location.GetProperty("link").GetString(), StringComparison.Ordinal);
        Assert.Equal("Address", location.GetProperty("address").GetString());
        using var retry = await second.PutAsJsonAsync("/api/ai/business-information", request, Ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await saved.ReadJsonAsync()).GetRawText(), (await retry.ReadJsonAsync()).GetRawText());
        var changed = request with { Details = request.Details! with { Location = request.Details!.Location with { PlaceId = "ChIJ_other" } } };
        using var conflict = await second.PutAsJsonAsync("/api/ai/business-information", changed, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Place_search_returns_only_suggestions_and_validates_before_calling_the_provider(string culture)
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var response = await client.PostAsJsonAsync("/api/ai/business-information/places/search", new { input = "Clinic Example City" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal("ChIJ_fictional", json.GetProperty("places")[0].GetProperty("placeId").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var invalid = await client.PostAsJsonAsync("/api/ai/business-information/places/search", new { input = "ab" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.ReadJsonAsync();
        Assert.Contains(culture == "es" ? "Ingresá" : "Enter", problem.GetProperty("errors").GetProperty("input")[0].GetString(), StringComparison.Ordinal);
        using var malformed = await client.PostAsync("/api/ai/business-information/places/search", new StringContent("{", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Request.Invalid", (await malformed.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Structured_facts_persist_across_hosts_and_retries_include_the_complete_payload()
    {
        using var client = await AdminAsync();
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        var request = GuidedPayload(revision, Guid.NewGuid());
        using var saved = await client.PutAsJsonAsync("/api/ai/business-information", request, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        using var second = secondHost.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var read = await ReadAsync(second);
        Assert.Equal("Legacy hours", read.GetProperty("hours").GetString());
        Assert.Equal("Legacy location", read.GetProperty("location").GetString());
        Assert.Equal("Legacy FAQ", read.GetProperty("faq").GetString());
        var details = read.GetProperty("details");
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=-34.6037%2C-58.3816", details.GetProperty("location").GetProperty("link").GetString());
        Assert.Equal("Address", details.GetProperty("location").GetProperty("address").GetString());
        Assert.Equal("22:00", details.GetProperty("openingHours").GetProperty("days")[0].GetProperty("periods")[0].GetProperty("start").GetString());
        Assert.Equal("2026-10-10", details.GetProperty("openingHours").GetProperty("exceptions")[0].GetProperty("date").GetString());
        Assert.Equal("", details.GetProperty("faq")[0].GetProperty("answer").GetString());
        using var retry = await second.PutAsJsonAsync("/api/ai/business-information", request, Ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await saved.ReadJsonAsync()).GetRawText(), (await retry.ReadJsonAsync()).GetRawText());
        var changed = request with { Details = request.Details! with { Location = request.Details!.Location with { Latitude = -34.604 } } };
        using var conflict = await second.PutAsJsonAsync("/api/ai/business-information", changed, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(read.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var legacy = await client.PutAsJsonAsync("/api/ai/business-information", Payload(revision + 1, Guid.NewGuid(), "Later description"), Ct);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(details.GetRawText(), (await ReadAsync(second)).GetProperty("details").GetRawText());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Invalid_structured_fields_are_localized_and_do_not_commit_or_create_a_receipt(string culture)
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var before = await ReadAsync(client);
        var request = GuidedPayload(before.GetProperty("revision").GetInt64(), Guid.NewGuid());
        request = request with { Details = request.Details! with { Location = request.Details!.Location with { Latitude = 91 } } };
        using var invalid = await client.PutAsJsonAsync("/api/ai/business-information", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.ReadJsonAsync();
        Assert.True(error.GetProperty("errors").TryGetProperty("details.location", out var messages));
        Assert.Contains(culture == "es" ? "Revisá" : "Review", messages[0].GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Address", error.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var missing = await client.GetAsync($"/api/ai/business-information/changes/{request.RequestId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static UpdateAiBusinessInformationHttpRequest GuidedPayload(long revision, Guid id) =>
        new("Shop", "", "Legacy hours", "Legacy location", "", "", "Legacy FAQ", revision, id, new(
            new("local", " Address ", "", "", -34.6037, -58.3816),
            new("America/Argentina/Buenos_Aires", Enumerable.Range(0, 7).Select(weekday =>
                new BusinessOpeningDayHttpRequest(weekday, weekday == 0 ? "open" : "unknown",
                    weekday == 0 ? [new("22:00", "02:00", true)] : [])).ToArray(),
                [new(new DateOnly(2026, 10, 10), "closed", [])]),
            [new(Guid.NewGuid(), "Question", "")]));

    [Theory]
    [InlineData("GET", "/api/ai/business-information")]
    [InlineData("PUT", "/api/ai/business-information")]
    [InlineData("GET", "/api/ai/business-information/changes/00000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/api/ai/business-information/places/search")]
    public async Task Every_route_requires_a_session_and_its_own_business_permissions(string method, string path)
    {
        using var client = factory.CreateClient();
        using var anonymous = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = method == "GET" ? null : JsonContent.Create(new { }) }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var connectionManager = await OperatorAsync([ArtificialIntelligencePermissions.Read, ArtificialIntelligencePermissions.Manage]);
        using var denied = await connectionManager.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = method == "GET" ? null : JsonContent.Create(new { }) }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Read_and_manage_are_separate_and_receipts_are_private_to_the_operator()
    {
        using var reader = await OperatorAsync([ArtificialIntelligencePermissions.BusinessRead]);
        using var read = await reader.GetAsync("/api/ai/business-information", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var write = await reader.PutAsJsonAsync("/api/ai/business-information", new { }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        using var deniedSearch = await reader.PostAsJsonAsync("/api/ai/business-information/places/search", new { input = "Clinic Example City" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSearch.StatusCode);
        using var manageOnly = await OperatorAsync([ArtificialIntelligencePermissions.BusinessManage]);
        using var noRead = await manageOnly.GetAsync("/api/ai/business-information", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, noRead.StatusCode);
        using var deniedManageSearch = await manageOnly.PostAsJsonAsync("/api/ai/business-information/places/search", new { input = "Clinic Example City" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedManageSearch.StatusCode);
        using var admin = await AdminAsync();
        var revision = (await ReadAsync(admin)).GetProperty("revision").GetInt64();
        var id = Guid.NewGuid();
        using var saved = await admin.PutAsJsonAsync("/api/ai/business-information", Payload(revision, id, "Private facts"), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var another = await OperatorAsync([ArtificialIntelligencePermissions.BusinessRead, ArtificialIntelligencePermissions.BusinessManage]);
        using var hidden = await another.GetAsync($"/api/ai/business-information/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var catalog = await admin.GetAsync("/api/permissions", Ct);
        var text = await catalog.Content.ReadAsStringAsync(Ct);
        Assert.Contains("ai.business.read", text, StringComparison.Ordinal);
        Assert.Contains("ai.business.manage", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Invalid_facts_return_localized_field_errors_without_echoing_content(string culture)
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var before = await ReadAsync(client);
        using var result = await client.PutAsJsonAsync("/api/ai/business-information", new { name = "",
            description = new string('x', 1001), hours = "", location = "", services = "", policies = "", faq = "",
            expectedRevision = before.GetProperty("revision").GetInt64(), requestId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var error = await result.ReadJsonAsync();
        Assert.True(error.GetProperty("errors").TryGetProperty("name", out _));
        Assert.True(error.GetProperty("errors").TryGetProperty("description", out _));
        Assert.True(error.TryGetProperty("traceId", out _));
        Assert.DoesNotContain(new string('x', 20), error.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
    }

    [Fact]
    public async Task Facts_are_audited_and_restorable_receipts_survive_a_host_restart_without_storing_content()
    {
        using var client = await AdminAsync();
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        var id = Guid.NewGuid();
        using var saved = await client.PutAsJsonAsync("/api/ai/business-information", Payload(revision, id, "Confirmed repairs"), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var row = await factory.ExecuteDbContextAsync(db => db.Set<AiBusinessInformation>().SingleAsync(Ct));
        Assert.NotNull(row.CreatedBy);
        Assert.Equal("Confirmed repairs", row.Description);
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        using var other = restarted.CreateClient();
        other.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var receipt = await other.GetAsync($"/api/ai/business-information/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var result = await receipt.ReadJsonAsync();
        Assert.Equal(revision + 1, result.GetProperty("revision").GetInt64());
        Assert.False(result.TryGetProperty("description", out _));
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<AiBusinessInformationChange>().SingleAsync(x => x.RequestId == id, Ct));
        Assert.DoesNotContain("Confirmed repairs", stored.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_receipts_cannot_reapply_an_old_revision()
    {
        using var client = await AdminAsync();
        var revision = (await ReadAsync(client)).GetProperty("revision").GetInt64();
        var id = Guid.NewGuid();
        var payload = Payload(revision, id, "Expired facts");
        using var saved = await client.PutAsJsonAsync("/api/ai/business-information", payload, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var future = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(factory.Clock.GetUtcNow().AddDays(31));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(future);
        }));
        using var after = host.CreateClient();
        after.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var receipt = await after.GetAsync($"/api/ai/business-information/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
        using var stale = await after.PutAsJsonAsync("/api/ai/business-information", payload, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var old = await factory.ExecuteDbContextAsync(db => db.Set<AiBusinessInformationChange>().SingleAsync(x => x.RequestId == id, Ct));
        // Failed writes roll back cleanup too. A later successful write removes expired receipts.
        using var fresh = await after.PutAsJsonAsync("/api/ai/business-information", Payload(revision + 1, Guid.NewGuid(), "Current"), Ct);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<AiBusinessInformationChange>().AnyAsync(x => x.Id == old.Id, Ct)));
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
        var responses = await Task.WhenAll(client.PutAsJsonAsync("/api/ai/business-information", payload, Ct), second.PutAsJsonAsync("/api/ai/business-information", payload, Ct));
        using var first = responses[0];
        using var retry = responses[1];
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await first.ReadJsonAsync()).GetRawText(), (await retry.ReadJsonAsync()).GetRawText());
        Assert.Equal(revision + 1, (await ReadAsync(second)).GetProperty("revision").GetInt64());
        using var reused = await second.PutAsJsonAsync("/api/ai/business-information", Payload(revision, id, "fictional-key-b"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var receipt = await client.GetAsync($"/api/ai/business-information/changes/{id}", Ct);
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
        var results = await Task.WhenAll(client.PutAsJsonAsync("/api/ai/business-information", Payload(revision, Guid.NewGuid(), "fictional-a"), Ct),
            other.PutAsJsonAsync("/api/ai/business-information", Payload(revision, Guid.NewGuid(), "fictional-b"), Ct));
        using var first = results[0];
        using var second = results[1];
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(x => x.StatusCode).Order());
        Assert.Equal(revision + 1, (await ReadAsync(other)).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task Failed_commit_rolls_back_the_information_and_its_receipt_together()
    {
        using var client = await AdminAsync();
        var before = await ReadAsync(client);
        var id = Guid.NewGuid();
        var probe = new CommitFailureProbe();
        await using var failingHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => FailingCommitUnitOfWork.Replace(services, probe)));
        using var failing = failingHost.CreateClient();
        failing.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var response = await failing.PutAsJsonAsync("/api/ai/business-information", Payload(before.GetProperty("revision").GetInt64(), id, "fictional-rollback"), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(probe.RolledBackBeforeLeaving);
        Assert.Equal(before.GetRawText(), (await ReadAsync(client)).GetRawText());
        using var receipt = await client.GetAsync($"/api/ai/business-information/changes/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
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
        using var response = await client.GetAsync("/api/ai/business-information", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
    private static object Payload(long revision, Guid id, string value) => new
        { name = "Shop", description = value, hours = "", location = "", services = "", policies = "", faq = "", expectedRevision = revision, requestId = id };
}
