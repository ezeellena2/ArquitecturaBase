using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Overview_excludes_an_unpublished_draft_from_rows_and_facets()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead]);
        var saved = await CreateAsync(actor, Payload("Overview unpublished " + Guid.NewGuid().ToString("N")));
        await AssertOverviewExcludedAsync(actor, saved.GetProperty("definition").GetProperty("prefix").GetString()!);
    }

    [Theory]
    [InlineData(FormPublicationState.Accepted)]
    [InlineData(FormPublicationState.Publishing)]
    [InlineData(FormPublicationState.Uncertain)]
    [InlineData(FormPublicationState.ActivationPending)]
    public async Task Overview_excludes_a_first_open_publication_without_an_active_version(FormPublicationState state)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead]);
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var client = DeactivationClient(host, actor, "es");
        var saved = await CreateAsync(client, Payload("Overview first publication " + Guid.NewGuid().ToString("N")));
        var definition = saved.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var operationId = (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid();
        if (state != FormPublicationState.Accepted)
        {
            using var scope = host.Services.CreateScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var reserved = await dispatch.ReserveAsync(operationId, Ct);
            Assert.True(reserved.IsSuccess);
            var work = Assert.IsType<FormPublicationWorkResponse>(reserved.Value);
            if (state == FormPublicationState.ActivationPending)
            {
                // Persist the valid domain transition before activation; CompleteAsync normally commits both atomically.
                await factory.ExecuteDbContextAsync(async db =>
                {
                    var operation = await db.Set<BusinessFormPublicationOperation>().SingleAsync(row => row.Id == operationId, Ct);
                    Assert.True(operation.RecordChannelResult(work.Fence, clock.GetUtcNow().UtcDateTime, "Published", "Activation").IsSuccess);
                    await db.SaveChangesAsync(Ct);
                });
            }
            else
            {
                var progress = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId,
                    work.Manifest.PublicationFingerprint, state.ToString(), "Verify", [], null, false);
                Assert.True((await dispatch.CompleteAsync(work, [new("test", progress)], Ct)).IsSuccess);
            }
        }
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(state, await db.Set<BusinessFormPublicationOperation>().Where(row => row.Id == operationId).Select(row => row.State).SingleAsync(Ct));
            Assert.Null(await db.Set<BusinessFormDefinition>().Where(row => row.Id == id).Select(row => row.ActiveVersionId).SingleAsync(Ct));
        });
        await AssertOverviewExcludedAsync(client, definition.GetProperty("prefix").GetString()!);
    }

    [Fact]
    public async Task Overview_excludes_an_empty_published_form_after_its_real_delete()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, actor, "es");
        var name = "Overview deleted " + Guid.NewGuid().ToString("N");
        var published = await PublishSuggestionFormAsync(host, client, name);
        var prefix = published.Saved.GetProperty("definition").GetProperty("prefix").GetString()!;
        await AssertOverviewPublishedAsync(client, prefix, published.Id, name);
        using var deleted = await DeleteDefinitionAsync(client, published.Id, 1);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertOverviewExcludedAsync(client, prefix);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Overview_keeps_the_active_published_name_while_a_secondary_language_is_removed_from_an_open_replacement(string culture)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, actor, culture);
        var identity = Guid.NewGuid().ToString("N");
        var originalName = "Overview original " + identity;
        var englishName = "Overview English " + identity;
        var payload = Payload(originalName); payload["name"]!["en"] = englishName;
        var saved = await CreateAsync(client, payload);
        var definition = saved.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var prefix = definition.GetProperty("prefix").GetString()!;
        var firstAttempt = await PublicationAttemptAsync(client, id, saved);
        using var first = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", firstAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var activeVersion = await CompletePublicationAsync(host, (await first.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
        var edit = EditPayload(saved);
        edit["name"] = new JsonObject { ["es"] = "Overview replacement " + identity, ["en"] = "" };
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var replacement = await PublicationAttemptAsync(client, id, await edited.ReadJsonAsync());
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", replacement, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var replacementOperationId = receipt.GetProperty("operationId").GetGuid();
        Assert.NotEqual(activeVersion, receipt.GetProperty("versionId").GetGuid());
        await AssertOverviewPublishedAsync(client, prefix, id, culture == "en" ? englishName : originalName);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(activeVersion, await db.Set<BusinessFormDefinition>().Where(row => row.Id == id).Select(row => row.ActiveVersionId).SingleAsync(Ct));
            Assert.Equal(FormPublicationState.Accepted, await db.Set<BusinessFormPublicationOperation>()
                .Where(row => row.Id == replacementOperationId).Select(row => row.State).SingleAsync(Ct));
        });
    }

    private static async Task AssertOverviewExcludedAsync(HttpClient client, string prefix)
    {
        foreach (var state in new[] { "Active", "Deactivated" })
        {
            using var response = await client.GetAsync($"/api/forms/overview?search={prefix}&state={state}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.ReadJsonAsync();
            Assert.Empty(body.GetProperty("forms").GetProperty("items").EnumerateArray());
            Assert.Equal(0, body.GetProperty("forms").GetProperty("totalCount").GetInt32());
            Assert.Equal(0, body.GetProperty("filterCounts").GetProperty("active").GetInt32());
            Assert.Equal(0, body.GetProperty("filterCounts").GetProperty("deactivated").GetInt32());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("filterCounts").GetProperty("deactivatedPending").ValueKind);
        }
    }

    private static async Task AssertOverviewPublishedAsync(HttpClient client, string prefix, Guid id, string name)
    {
        using var response = await client.GetAsync($"/api/forms/overview?search={prefix}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        var row = OverviewRow(body);
        Assert.Equal(id, row.GetProperty("id").GetGuid());
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.Equal(0, row.GetProperty("responseCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lastResponse").ValueKind);
        Assert.Equal(1, body.GetProperty("forms").GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("filterCounts").GetProperty("active").GetInt32());
        Assert.Equal(0, body.GetProperty("filterCounts").GetProperty("deactivated").GetInt32());
    }
}