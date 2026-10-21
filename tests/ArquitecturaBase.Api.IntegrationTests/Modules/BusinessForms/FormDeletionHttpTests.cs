using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_an_empty_draft_or_published_form_keeps_audit_prefix_and_change_recovery(bool published)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var deleter = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var recipient = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var client = DeactivationClient(host, author, "es");
        using var deleteClient = DeactivationClient(host, deleter, "es");
        var search = "DEL" + Guid.NewGuid().ToString("N")[..8];
        var payload = Payload(search);
        payload["offerWhen"] = "Cuando la persona pida este formulario.";
        var saved = await CreateAsync(client, payload);
        var definition = saved.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var prefix = definition.GetProperty("prefix").GetString()!;
        Guid? versionId = null;
        if (published)
        {
            var attempt = await PublicationAttemptAsync(client, id, saved);
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            versionId = await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
            using var selectionScope = host.Services.CreateScope();
            var selections = selectionScope.ServiceProvider.GetRequiredService<IFormSelectionReader>();
            Assert.Contains(await selections.ListAssistantAsync(Ct), row => row.DefinitionId == id);
            Assert.NotNull(await selectionScope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
                .FindAsync(DeactivationActorId(recipient), "es", id, versionId.Value, Ct));
        }
        var before = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().AsNoTracking()
            .Where(row => row.Id == id).Select(row => new { row.CreatedAtUtc, row.CreatedBy, row.SchemaJson }).SingleAsync(Ct));
        using var stale = await DeleteDefinitionAsync(deleteClient, id, 2);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(FormDraftErrors.RevisionConflict.Code, (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        using var deleted = await DeleteDefinitionAsync(deleteClient, id, 1);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await deleted.Content.ReadAsByteArrayAsync(Ct));
        var deletedAt = clock.GetUtcNow().UtcDateTime;
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<BusinessFormDefinition>().AnyAsync(row => row.Id == id, Ct));
            var stored = await db.Set<BusinessFormDefinition>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == id, Ct);
            Assert.True(stored.IsDeleted);
            Assert.Equal(deletedAt, stored.DeletedAtUtc);
            Assert.Equal(DateTimeKind.Utc, stored.DeletedAtUtc!.Value.Kind);
            Assert.Equal(DeactivationActorId(deleter), stored.DeletedBy);
            Assert.Equal(before.CreatedAtUtc, stored.CreatedAtUtc);
            Assert.Equal(before.CreatedBy, stored.CreatedBy);
            Assert.Equal(before.SchemaJson, stored.SchemaJson);
            Assert.Equal(prefix, stored.Prefix);
            Assert.Equal(versionId, stored.ActiveVersionId);
            Assert.Equal(0, stored.LastRecordNumber);
            Assert.Equal(published ? 1 : 0, await db.Set<BusinessFormVersion>().CountAsync(row => row.DefinitionId == id, Ct));
        });
        foreach (var culture in new[] { "es", "en" })
        {
            deleteClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            deleteClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var detail = await deleteClient.GetAsync($"/api/forms/definitions/{id}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            Assert.Equal(FormDefinitionErrors.NotFound.Code, (await detail.ReadJsonAsync()).GetProperty("code").GetString());
            foreach (var filter in new[] { "Active", "Published", "Drafts", "Deactivated" })
            {
                using var list = await deleteClient.GetAsync($"/api/forms/definitions?search={search}&publicationFilter={filter}", Ct);
                Assert.Equal(HttpStatusCode.OK, list.StatusCode);
                var page = await list.ReadJsonAsync();
                Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
                Assert.Empty(page.GetProperty("items").EnumerateArray());
            }
            using var facets = await deleteClient.GetAsync($"/api/forms/definitions/facets?search={search}", Ct);
            Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
            var counts = await facets.ReadJsonAsync();
            foreach (var filter in new[] { "active", "published", "drafts", "deactivated" }) Assert.Equal(0, counts.GetProperty(filter).GetInt32());
        }
        using (var scope = host.Services.CreateScope())
        {
            var selections = scope.ServiceProvider.GetRequiredService<IFormSelectionReader>();
            Assert.DoesNotContain(await selections.ListAssistantAsync(Ct), row => row.DefinitionId == id);
            Assert.Empty(await selections.ListSuggestionTargetsAsync([id], Ct));
            var forms = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
            Assert.Empty((await forms.ListAsync(DeactivationActorId(recipient), "es", new ListStructuredFormsRequest { Search = search }, Ct)).Items);
            if (versionId is { } version) Assert.Null(await forms.FindAsync(DeactivationActorId(recipient), "es", id, version, Ct));
        }
        var reusedPrefix = Payload("Otro formulario");
        reusedPrefix["prefix"] = prefix;
        using var duplicate = await client.PostAsJsonAsync("/api/forms/definitions", reusedPrefix, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var prefixError = await duplicate.ReadJsonAsync();
        Assert.Equal(FormDefinitionErrors.PrefixTaken.Code, prefixError.GetProperty("code").GetString());
        Assert.True(prefixError.GetProperty("errors").TryGetProperty("prefix", out _));
        using var recovered = await client.GetAsync("/api/forms/draft-changes/" + saved.GetProperty("requestId").GetGuid(), Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var recoveredBody = await recovered.ReadJsonAsync();
        Assert.Equal(saved.GetRawText(), recoveredBody.GetProperty("result").GetRawText());
        Assert.Equal(1, recoveredBody.GetProperty("currentRevision").GetInt64());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().IgnoreQueryFilters()
            .CountAsync(row => row.Prefix == prefix, Ct)));
        using var repeated = await DeleteDefinitionAsync(deleteClient, id, 1);
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
    }

    [Fact]
    public async Task A_deleted_suggestion_destination_stays_in_the_current_set_as_unavailable_until_removed()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var deleter = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, author, "es");
        using var deleteClient = DeactivationClient(host, deleter, "es");
        using var suggestionClient = DeactivationClient(host, manager, "es");
        var source = await PublishSuggestionFormAsync(host, client, "Origen con destino eliminado " + Guid.NewGuid().ToString("N")[..8]);
        var target = await PublishSuggestionFormAsync(host, client, "Destino eliminado " + Guid.NewGuid().ToString("N")[..8]);
        var fieldId = target.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();
        var suggestion = DeactivationSuggestion(target.Id, fieldId);
        var configured = await SaveDeactivationSuggestionsAsync(suggestionClient, source.Id, 0, suggestion);
        using var deleted = await DeleteDefinitionAsync(deleteClient, target.Id, 1);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var route = $"/api/forms/definitions/{source.Id}/suggestions";
        foreach (var culture in new[] { "es", "en" })
        {
            suggestionClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            suggestionClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var options = await suggestionClient.GetAsync(route + "/options", Ct);
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var body = await options.ReadJsonAsync();
            Assert.DoesNotContain(body.GetProperty("destinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
            var unavailable = Assert.Single(body.GetProperty("unavailableDestinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
            Assert.Equal("Deleted", unavailable.GetProperty("state").GetString());
            Assert.Equal(target.Saved.GetProperty("definition").GetProperty("name").GetProperty(culture).GetString(), unavailable.GetProperty("name").GetProperty(culture).GetString());
            await FormLifecycleContractCapture.WriteAsync(options, culture, "options-deleted", Ct);
        }
        using var unchanged = await suggestionClient.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(configured.GetRawText(), (await unchanged.ReadJsonAsync()).GetRawText());
        using var blocked = await suggestionClient.PostAsJsonAsync(route, new { expectedRevision = 1, suggestions = new[] { suggestion } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(FormSuggestionErrors.ContentInvalid.Code, (await blocked.ReadJsonAsync()).GetProperty("code").GetString());
        using var stillCurrent = await suggestionClient.GetAsync(route, Ct);
        Assert.Equal(configured.GetRawText(), (await stillCurrent.ReadJsonAsync()).GetRawText());
        var removed = await SaveDeactivationSuggestionsAsync(suggestionClient, source.Id, 1);
        Assert.Equal(2, removed.GetProperty("revision").GetInt64());
        Assert.Empty(removed.GetProperty("suggestions").EnumerateArray());
        using var afterRemoval = await suggestionClient.GetAsync(route + "/options", Ct);
        Assert.DoesNotContain((await afterRemoval.ReadJsonAsync()).GetProperty("unavailableDestinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
        using var oldSet = await suggestionClient.GetAsync(route + "/revisions/1", Ct);
        Assert.Equal(configured.GetRawText(), (await oldSet.ReadJsonAsync()).GetRawText());
    }

    [Fact]
    public async Task A_form_with_a_real_response_cannot_be_deleted_and_keeps_its_record_and_number()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var recipient = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, author, "es");
        var form = await PublishSuggestionFormAsync(host, client, "Respuesta conservada " + Guid.NewGuid().ToString("N")[..8]);
        var key = form.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var record = await CaptureDeactivationRecordAsync(host, form.Id, form.VersionId, key, DeactivationActorId(recipient), "es");
        foreach (var culture in new[] { "es", "en" })
        {
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var rejected = await DeleteDefinitionAsync(client, form.Id, 1);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal(FormDefinitionErrors.HasResponses.Code, (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
            await FormLifecycleContractCapture.WriteAsync(rejected, culture, "delete-responses-conflict", Ct);
        }
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(row => row.Id == form.Id, Ct);
            Assert.False(definition.IsDeleted);
            Assert.Null(definition.DeletedBy);
            Assert.Null(definition.DeletedAtUtc);
            Assert.Equal(1, definition.LastRecordNumber);
            Assert.Equal(1, definition.Revision);
            Assert.True(await db.Set<BusinessFormRecord>().AnyAsync(row => row.Id == record.RecordId && row.DefinitionId == form.Id, Ct));
        });
    }

    [Fact]
    public async Task An_accepted_publication_blocks_deletion_without_removing_its_draft_or_operation()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, author, "es");
        var saved = await CreateAsync(client, Payload("No borrar mientras publica " + Guid.NewGuid().ToString("N")[..8]));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        using var rejected = await DeleteDefinitionAsync(client, id, 1);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(FormPublicationErrors.InProgress.Code, (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(1, body.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("publication").GetProperty("activeVersion").ValueKind);
        Assert.Equal("Accepted", body.GetProperty("publication").GetProperty("currentOperation").GetProperty("state").GetString());
        Assert.False(body.GetProperty("lifecycle").GetProperty("canDelete").GetBoolean());
        Assert.Equal("PublicationInProgress", body.GetProperty("lifecycle").GetProperty("deletionUnavailableReason").GetString());
        await CompletePublicationAsync(host, receipt.GetProperty("operationId").GetGuid(), id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Open_channel_sessions_or_pending_replies_block_deletion_until_the_channel_clears_them(bool openSession, bool pendingReply)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var state = new ControlledFormChannelStateSource();
        await using var publicationHost = PublicationHost(clock);
        await using var host = publicationHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannelStateSource>();
            services.AddSingleton<IStructuredFormChannelStateSource>(state);
        }));
        using var client = DeactivationClient(host, author, "es");
        var form = await PublishSuggestionFormAsync(host, client, "Envío protegido " + Guid.NewGuid().ToString("N")[..8]);
        var expiry = openSession ? clock.GetUtcNow().UtcDateTime.AddHours(1) : (DateTime?)null;
        state.Rows = [new(form.Id, expiry, pendingReply)];
        using var detail = await client.GetAsync($"/api/forms/definitions/{form.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var lifecycle = (await detail.ReadJsonAsync()).GetProperty("lifecycle");
        Assert.False(lifecycle.GetProperty("canDelete").GetBoolean());
        Assert.Equal("OpenSends", lifecycle.GetProperty("deletionUnavailableReason").GetString());
        if (expiry is { } instant) Assert.Equal(instant, lifecycle.GetProperty("lastOpenSessionExpiresAtUtc").GetDateTime());
        using var rejected = await DeleteDefinitionAsync(client, form.Id, 1);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(FormDefinitionErrors.OpenSends.Code, (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(row => row.Id == form.Id).Select(row => row.IsDeleted).SingleAsync(Ct)));
        state.Rows = [];
        using var deleted = await DeleteDefinitionAsync(client, form.Id, 1);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    private static async Task<HttpResponseMessage> DeleteDefinitionAsync(HttpClient client, Guid definitionId, long expectedRevision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{definitionId}")
        { Content = JsonContent.Create(new { expectedRevision }) };
        return await client.SendAsync(request, Ct);
    }
}
