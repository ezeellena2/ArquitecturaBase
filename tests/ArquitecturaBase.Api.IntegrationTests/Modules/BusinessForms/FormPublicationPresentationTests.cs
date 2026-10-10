using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Reload_recovers_publication_state_but_discloses_the_request_only_to_its_currently_authorized_owner()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Reload publication"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        using var reloaded = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var detail = await reloaded.ReadJsonAsync();
        var publication = detail.GetProperty("publication");
        Assert.Equal("Accepted", publication.GetProperty("displayState").GetString());
        Assert.Equal(JsonValueKind.Null, publication.GetProperty("activeVersion").ValueKind);
        Assert.Equal(receipt.GetProperty("requestId").GetGuid(), publication.GetProperty("currentOperation").GetProperty("requestId").GetGuid());
        Assert.Equal(accepted.Headers.Location!.OriginalString, publication.GetProperty("currentOperation").GetProperty("recoveryPath").GetString());
        Assert.True(detail.GetProperty("capabilities").GetProperty("canPublish").GetBoolean());
        Assert.False(detail.GetProperty("capabilities").GetProperty("canPreview").GetBoolean());
        foreach (var permissions in new[] { new[] { BusinessFormsPermissions.Read }, new[] { BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish } })
        {
            using var reader = await OperatorAsync(permissions);
            using var other = await reader.GetAsync($"/api/forms/definitions/{id}", Ct);
            var visible = (await other.ReadJsonAsync()).GetProperty("publication");
            Assert.Equal("Accepted", visible.GetProperty("displayState").GetString());
            Assert.Equal(JsonValueKind.Null, visible.GetProperty("currentOperation").GetProperty("requestId").ValueKind);
            Assert.Equal(JsonValueKind.Null, visible.GetProperty("currentOperation").GetProperty("recoveryPath").ValueKind);
            Assert.DoesNotContain(receipt.GetProperty("requestId").GetGuid().ToString(), visible.GetRawText(), StringComparison.Ordinal);
        }
        using var listed = await client.GetAsync("/api/forms/definitions?search=" + saved.GetProperty("definition").GetProperty("prefix").GetString(), Ct);
        Assert.Equal("Accepted", (await listed.ReadJsonAsync()).GetProperty("items")[0].GetProperty("displayState").GetString());
    }

    [Fact]
    public async Task A_new_pending_or_failed_publication_preserves_the_previous_active_version_and_published_facets()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Publication history"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var firstAttempt = await PublicationAttemptAsync(client, id, saved);
        using var first = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", firstAttempt, Ct);
        var firstReceipt = await first.ReadJsonAsync();
        using (var scope = host.Services.CreateScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var work = (await dispatch.ReserveAsync(firstReceipt.GetProperty("operationId").GetGuid(), Ct)).Value!;
            var response = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId, work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
            Assert.True((await dispatch.CompleteAsync(work, [new("test", response)], Ct)).IsSuccess);
        }
        var channel = Assert.IsType<ReadyStructuredFormChannel>(Assert.Single(host.Services.GetServices<IStructuredFormChannel>()));
        channel.PreviewLifetime = TimeSpan.Zero;
        using var expired = await client.GetAsync($"/api/forms/definitions/{id}/versions/{firstReceipt.GetProperty("versionId").GetGuid()}/preview?culture=es", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode); Assert.True(expired.Headers.CacheControl!.NoStore);
        var problem = await expired.ReadJsonAsync(); Assert.Equal("Forms.Preview.Expired", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("url", out _));
        channel.PreviewLifetime = TimeSpan.FromMinutes(5);
        using var renewed = await client.GetAsync($"/api/forms/definitions/{id}/versions/{firstReceipt.GetProperty("versionId").GetGuid()}/preview?culture=es", Ct);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", EditPayload(saved), Ct);
        var secondAttempt = await PublicationAttemptAsync(client, id, await edited.ReadJsonAsync());
        using var second = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", secondAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var secondReceipt = await second.ReadJsonAsync();
        foreach (var state in new[] { "Accepted", "Failed" })
        {
            if (state == "Failed")
            {
                using var scope = host.Services.CreateScope(); var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
                var work = (await dispatch.ReserveAsync(secondReceipt.GetProperty("operationId").GetGuid(), Ct)).Value!;
                var response = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId, work.Manifest.PublicationFingerprint, "Failed", "Validation", [], null, false);
                Assert.True((await dispatch.CompleteAsync(work, [new("test", response)], Ct)).IsSuccess);
            }
            using var reloaded = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
            var publication = (await reloaded.ReadJsonAsync()).GetProperty("publication");
            Assert.Equal(state, publication.GetProperty("displayState").GetString());
            Assert.Equal(firstReceipt.GetProperty("versionId").GetGuid(), publication.GetProperty("activeVersion").GetProperty("versionId").GetGuid());
            var search = saved.GetProperty("definition").GetProperty("prefix").GetString();
            using var listed = await client.GetAsync("/api/forms/definitions?publicationFilter=Published&search=" + search, Ct);
            var row = (await listed.ReadJsonAsync()).GetProperty("items")[0];
            Assert.Equal(state, row.GetProperty("displayState").GetString()); Assert.Equal(1, row.GetProperty("activeVersionNumber").GetInt64());
            using var facets = await client.GetAsync("/api/forms/definitions/facets?search=" + search, Ct);
            var counts = await facets.ReadJsonAsync(); Assert.Equal(1, counts.GetProperty("published").GetInt32()); Assert.Equal(0, counts.GetProperty("drafts").GetInt32());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?culture=fr")]
    [InlineData("?culture=es-AR")]
    public async Task Official_preview_requires_an_explicit_supported_culture(string query)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var response = await actor.GetAsync($"/api/forms/definitions/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/preview" + query, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("culture", out _));
    }
}
