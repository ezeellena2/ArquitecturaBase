using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Deactivation_keeps_history_and_reactivation_publishes_a_new_version_without_resetting_numbers(string culture)
    {
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead]);
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var editor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);
        using var recipient = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var recipientId = DeactivationActorId(recipient);
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>()
            .UpdateProfileAsync(recipientId, "Persona de prueba", culture, "America/Argentina/Buenos_Aires", Ct));
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var authorClient = DeactivationClient(host, designer, culture);
        using var publisherClient = DeactivationClient(host, publisher, culture);
        using var editorClient = DeactivationClient(host, editor, culture);
        using var suggestionClient = DeactivationClient(host, manager, culture);
        var search = "BAJA" + Guid.NewGuid().ToString("N")[..8];
        var target = await PublishSuggestionFormAsync(host, authorClient, search + " destino");
        var source = await PublishSuggestionFormAsync(host, authorClient, search + " origen");
        var draft = await CreateAsync(authorClient, Payload(search + " borrador"));
        var draftId = draft.GetProperty("definition").GetProperty("id").GetGuid();
        var targetField = target.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0];
        var targetKey = targetField.GetProperty("key").GetString()!;
        var prefix = target.Saved.GetProperty("definition").GetProperty("prefix").GetString()!;
        var targetEdit = EditPayload(target.Saved);
        targetEdit["offerWhen"] = "Cuando la persona solicite este formulario.";
        using var instruction = await authorClient.PutAsJsonAsync($"/api/forms/definitions/{target.Id}/draft", targetEdit, Ct);
        Assert.Equal(HttpStatusCode.OK, instruction.StatusCode);
        var currentDraft = await instruction.ReadJsonAsync();
        Assert.Equal(2, currentDraft.GetProperty("definition").GetProperty("revision").GetInt64());
        var suggestion = DeactivationSuggestion(target.Id, targetField.GetProperty("fieldId").GetGuid());
        var currentSet = await SaveDeactivationSuggestionsAsync(suggestionClient, source.Id, 0, suggestion);
        var sourceKey = source.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var sourceRecord = await CaptureDeactivationRecordAsync(host, source.Id, source.VersionId, sourceKey, recipientId, culture);
        Assert.Equal(1, (await CaptureDeactivationRecordAsync(host, target.Id, target.VersionId, targetKey, recipientId, culture)).Number);
        using (var scope = host.Services.CreateScope())
        {
            var selections = scope.ServiceProvider.GetRequiredService<IFormSelectionReader>();
            Assert.Contains(await selections.ListAssistantAsync(Ct), row => row.DefinitionId == target.Id);
            Assert.NotNull(await selections.FindAssistantAsync(target.Id, target.VersionId, Ct));
            var forms = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
            Assert.Equal(new[] { target.Id, source.Id }.Order(), (await forms.ListAsync(recipientId, culture,
                new ListStructuredFormsRequest { Search = search }, Ct)).Items.Select(row => row.DefinitionId).Order());
            var candidates = await forms.ListSuggestionsAsync(recipientId, culture, sourceRecord.RecordId!.Value, Ct);
            Assert.True(candidates.IsSuccess);
            Assert.Equal(target.Id, Assert.Single(candidates.Value.Candidates).Form.DefinitionId);
        }
        var deactivationRoute = $"/api/forms/definitions/{target.Id}/deactivation";
        using var preview = await publisherClient.GetAsync(deactivationRoute, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True(preview.Headers.CacheControl!.NoStore);
        var previewBody = await preview.ReadJsonAsync();
        Assert.Equal(2, previewBody.GetProperty("revision").GetInt64());
        Assert.True(previewBody.GetProperty("lifecycle").GetProperty("canDeactivate").GetBoolean());
        AssertDeactivationRecordsHidden(previewBody.GetProperty("lifecycle"));
        await FormLifecycleContractCapture.WriteAsync(preview, culture, "preview", Ct);
        using var stale = await publisherClient.PostAsJsonAsync(deactivationRoute, new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(FormDraftErrors.RevisionConflict.Code, (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        using var deactivated = await publisherClient.PostAsJsonAsync(deactivationRoute, new { expectedRevision = 2 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.True(deactivated.Headers.CacheControl!.NoStore);
        var deactivatedBody = await deactivated.ReadJsonAsync();
        Assert.Equal(3, deactivatedBody.GetProperty("revision").GetInt64());
        var deactivatedAt = clock.GetUtcNow().UtcDateTime;
        Assert.Equal(deactivatedAt, deactivatedBody.GetProperty("lifecycle").GetProperty("deactivatedAtUtc").GetDateTime());
        Assert.Equal(DeactivationActorId(publisher), deactivatedBody.GetProperty("lifecycle").GetProperty("deactivatedBy").GetProperty("userId").GetGuid());
        Assert.True(deactivatedBody.GetProperty("lifecycle").GetProperty("canReactivate").GetBoolean());
        AssertDeactivationRecordsHidden(deactivatedBody.GetProperty("lifecycle"));
        await FormLifecycleContractCapture.WriteAsync(deactivated, culture, "deactivated", Ct);
        using var repeated = await publisherClient.PostAsJsonAsync(deactivationRoute, new { expectedRevision = 2 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var detail = await authorClient.GetAsync($"/api/forms/definitions/{target.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.ReadJsonAsync();
        Assert.Equal(3, detailBody.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(target.VersionId, detailBody.GetProperty("publication").GetProperty("activeVersion").GetProperty("versionId").GetGuid());
        Assert.Equal(1, detailBody.GetProperty("lifecycle").GetProperty("responseCount").GetInt64());
        Assert.Equal(0, detailBody.GetProperty("lifecycle").GetProperty("pendingApprovalCount").GetInt32());
        Assert.Equal(prefix + "-000002", detailBody.GetProperty("lifecycle").GetProperty("nextRecordNumber").GetString());
        Assert.Equal(2, detailBody.GetProperty("lifecycle").GetProperty("nextVersionNumber").GetInt64());
        AssertDeactivationAudit(detailBody, DeactivationActorId(publisher), deactivatedAt);
        await FormLifecycleContractCapture.WriteAsync(detail, culture, "detail-deactivated", Ct);
        foreach (var (filter, expected) in new (string? Filter, Guid[] Ids)[]
        {
            (null, [source.Id, draftId]), ("Active", [source.Id, draftId]),
            ("Published", [source.Id]), ("Drafts", [draftId]), ("Deactivated", [target.Id])
        })
        {
            var route = "/api/forms/definitions?search=" + search + (filter is null ? "" : "&publicationFilter=" + filter);
            using var listed = await publisherClient.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var page = await listed.ReadJsonAsync();
            Assert.Equal(expected.Order(), page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Order());
            Assert.Equal(expected.Length, page.GetProperty("totalCount").GetInt32());
            foreach (var row in page.GetProperty("items").EnumerateArray())
            {
                Assert.False(row.GetProperty("canEdit").GetBoolean());
                AssertDeactivationRecordsHidden(row.GetProperty("lifecycle"));
            }
            if (filter == "Deactivated") await FormLifecycleContractCapture.WriteAsync(listed, culture, "list-deactivated", Ct);
        }
        using var facets = await publisherClient.GetAsync("/api/forms/definitions/facets?search=" + search, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        var counts = await facets.ReadJsonAsync();
        Assert.Equal(2, counts.GetProperty("active").GetInt32());
        Assert.Equal(1, counts.GetProperty("published").GetInt32());
        Assert.Equal(1, counts.GetProperty("drafts").GetInt32());
        Assert.Equal(1, counts.GetProperty("deactivated").GetInt32());
        await FormLifecycleContractCapture.WriteAsync(facets, culture, "facets", Ct);
        using (var scope = host.Services.CreateScope())
        {
            var selections = scope.ServiceProvider.GetRequiredService<IFormSelectionReader>();
            Assert.DoesNotContain(await selections.ListAssistantAsync(Ct), row => row.DefinitionId == target.Id);
            Assert.Null(await selections.FindAssistantAsync(target.Id, target.VersionId, Ct));
            Assert.Empty(await selections.ListSuggestionTargetsAsync([target.Id], Ct));
            var forms = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
            Assert.Null(await forms.FindAsync(recipientId, culture, target.Id, target.VersionId, Ct));
            Assert.NotNull(await forms.FindAcceptedSessionAsync(recipientId, culture, target.Id, target.VersionId, Ct));
            Assert.Equal(source.Id, Assert.Single((await forms.ListAsync(recipientId, culture,
                new ListStructuredFormsRequest { Search = search }, Ct)).Items).DefinitionId);
            var candidates = await forms.ListSuggestionsAsync(recipientId, culture, sourceRecord.RecordId!.Value, Ct);
            Assert.True(candidates.IsSuccess);
            Assert.Empty(candidates.Value.Candidates);
        }
        var suggestionsRoute = $"/api/forms/definitions/{source.Id}/suggestions";
        using var options = await suggestionClient.GetAsync(suggestionsRoute + "/options", Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var optionsBody = await options.ReadJsonAsync();
        Assert.Equal(source.VersionId, optionsBody.GetProperty("source").GetProperty("versionId").GetGuid());
        Assert.DoesNotContain(optionsBody.GetProperty("destinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
        var unavailable = Assert.Single(optionsBody.GetProperty("unavailableDestinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
        Assert.Equal("Deactivated", unavailable.GetProperty("state").GetString());
        Assert.Equal(target.Saved.GetProperty("definition").GetProperty("name").GetProperty(culture).GetString(), unavailable.GetProperty("name").GetProperty(culture).GetString());
        await FormLifecycleContractCapture.WriteAsync(options, culture, "options-unavailable", Ct);
        using var unchanged = await suggestionClient.GetAsync(suggestionsRoute, Ct);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(currentSet.GetRawText()), JsonNode.Parse((await unchanged.ReadJsonAsync()).GetRawText())));
        using var rejectedSet = await suggestionClient.PostAsJsonAsync(suggestionsRoute, new { expectedRevision = 1, suggestions = new[] { suggestion } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedSet.StatusCode);
        Assert.Equal(FormSuggestionErrors.ContentInvalid.Code, (await rejectedSet.ReadJsonAsync()).GetProperty("code").GetString());
        using var stillCurrent = await suggestionClient.GetAsync(suggestionsRoute, Ct);
        Assert.Equal(currentSet.GetRawText(), (await stillCurrent.ReadJsonAsync()).GetRawText());
        using var deactivatedSourceOptions = await suggestionClient.GetAsync($"/api/forms/definitions/{target.Id}/suggestions/options", Ct);
        Assert.Equal(HttpStatusCode.OK, deactivatedSourceOptions.StatusCode);
        Assert.Equal(target.VersionId, (await deactivatedSourceOptions.ReadJsonAsync()).GetProperty("source").GetProperty("versionId").GetGuid());
        Assert.Equal(2, (await CaptureDeactivationRecordAsync(host, target.Id, target.VersionId, targetKey, recipientId, culture)).Number);
        var edit = EditPayload(detailBody);
        edit["name"]!["es"] = search + " destino editado";
        edit["name"]!["en"] = search + " edited destination";
        using var savedAfterDeactivation = await editorClient.PutAsJsonAsync($"/api/forms/definitions/{target.Id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, savedAfterDeactivation.StatusCode);
        var editedDraft = await savedAfterDeactivation.ReadJsonAsync();
        Assert.Equal(4, editedDraft.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(deactivatedAt, clock.GetUtcNow().UtcDateTime);
        using var editedDetail = await authorClient.GetAsync($"/api/forms/definitions/{target.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, editedDetail.StatusCode);
        var editedBody = await editedDetail.ReadJsonAsync();
        AssertDeactivationAudit(editedBody, DeactivationActorId(editor), deactivatedAt);
        Assert.Equal(deactivatedAt, editedBody.GetProperty("lifecycle").GetProperty("deactivatedAtUtc").GetDateTime());
        Assert.Equal(2, editedBody.GetProperty("lifecycle").GetProperty("responseCount").GetInt64());
        var attempt = await PublicationAttemptAsync(publisherClient, target.Id, editedDraft);
        using var accepted = await publisherClient.PostAsJsonAsync($"/api/forms/definitions/{target.Id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var pending = await authorClient.GetAsync($"/api/forms/definitions/{target.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingBody = await pending.ReadJsonAsync();
        Assert.Equal(deactivatedAt, pendingBody.GetProperty("lifecycle").GetProperty("deactivatedAtUtc").GetDateTime());
        Assert.False(pendingBody.GetProperty("lifecycle").GetProperty("canReactivate").GetBoolean());
        Assert.Equal("PublicationInProgress", pendingBody.GetProperty("lifecycle").GetProperty("unavailableReason").GetString());
        Assert.Equal("Accepted", pendingBody.GetProperty("publication").GetProperty("currentOperation").GetProperty("state").GetString());
        Assert.Equal(target.VersionId, pendingBody.GetProperty("publication").GetProperty("activeVersion").GetProperty("versionId").GetGuid());
        await FormLifecycleContractCapture.WriteAsync(pending, culture, "detail-reactivation-pending", Ct);
        var newVersion = await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), target.Id);
        Assert.NotEqual(target.VersionId, newVersion);
        Assert.Equal(3, (await CaptureDeactivationRecordAsync(host, target.Id, newVersion, targetKey, recipientId, culture)).Number);
        using var reactivated = await authorClient.GetAsync($"/api/forms/definitions/{target.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        var reactivatedBody = await reactivated.ReadJsonAsync();
        var lifecycle = reactivatedBody.GetProperty("lifecycle");
        Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("deactivatedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("deactivatedBy").ValueKind);
        Assert.Equal(3, lifecycle.GetProperty("responseCount").GetInt64());
        Assert.Equal(prefix + "-000004", lifecycle.GetProperty("nextRecordNumber").GetString());
        Assert.Equal(4, reactivatedBody.GetProperty("definition").GetProperty("revision").GetInt64());
        var active = reactivatedBody.GetProperty("publication").GetProperty("activeVersion");
        Assert.Equal(newVersion, active.GetProperty("versionId").GetGuid());
        Assert.Equal(2, active.GetProperty("versionNumber").GetInt64());
        await FormLifecycleContractCapture.WriteAsync(reactivated, culture, "detail-reactivated", Ct);
        using (var scope = host.Services.CreateScope())
        {
            var forms = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
            Assert.NotNull(await forms.FindAsync(recipientId, culture, target.Id, newVersion, Ct));
            Assert.NotNull(await forms.FindAcceptedSessionAsync(recipientId, culture, target.Id, target.VersionId, Ct));
        }
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(row => row.Id == target.Id, Ct);
            Assert.Equal(prefix, definition.Prefix);
            Assert.Equal(3, definition.LastRecordNumber);
            Assert.Equal(2, await db.Set<BusinessFormVersion>().CountAsync(row => row.DefinitionId == target.Id, Ct));
            Assert.Equal(currentSet.GetProperty("id").GetGuid(), await db.Set<BusinessFormDefinition>().Where(row => row.Id == source.Id).Select(row => row.ActiveSuggestionSetId).SingleAsync(Ct));
        });
    }

    [Fact]
    public async Task Deactivation_preview_lists_only_current_active_suggestions_from_offerable_sources_once()
    {
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, designer, "es");
        using var publisherClient = DeactivationClient(host, publisher, "es");
        using var suggestionClient = DeactivationClient(host, manager, "es");
        var search = "INV" + Guid.NewGuid().ToString("N")[..8];
        var target = await PublishSuggestionFormAsync(host, client, search + " destino");
        var current = await PublishSuggestionFormAsync(host, client, search + " vigente");
        var replaced = await PublishSuggestionFormAsync(host, client, search + " reemplazado");
        var inactive = await PublishSuggestionFormAsync(host, client, search + " inactivo");
        var deactivated = await PublishSuggestionFormAsync(host, client, search + " baja");
        var fieldId = target.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();
        await SaveDeactivationSuggestionsAsync(suggestionClient, current.Id, 0,
            DeactivationSuggestion(target.Id, fieldId), DeactivationSuggestion(target.Id, fieldId));
        await SaveDeactivationSuggestionsAsync(suggestionClient, replaced.Id, 0, DeactivationSuggestion(target.Id, fieldId));
        await SaveDeactivationSuggestionsAsync(suggestionClient, replaced.Id, 1);
        await SaveDeactivationSuggestionsAsync(suggestionClient, inactive.Id, 0, DeactivationSuggestion(target.Id, fieldId, false));
        await SaveDeactivationSuggestionsAsync(suggestionClient, deactivated.Id, 0, DeactivationSuggestion(target.Id, fieldId));
        using var sourceDeactivation = await publisherClient.PostAsJsonAsync($"/api/forms/definitions/{deactivated.Id}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, sourceDeactivation.StatusCode);
        foreach (var culture in new[] { "es", "en" })
        {
            publisherClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            publisherClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var preview = await publisherClient.GetAsync($"/api/forms/definitions/{target.Id}/deactivation", Ct);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var body = await preview.ReadJsonAsync();
            var affected = Assert.Single(body.GetProperty("affectedSuggestions").EnumerateArray());
            Assert.Equal(current.Id, affected.GetProperty("definitionId").GetGuid());
            Assert.Equal(current.Saved.GetProperty("definition").GetProperty("name").GetProperty(culture).GetString(), affected.GetProperty("name").GetProperty(culture).GetString());
            AssertDeactivationRecordsHidden(body.GetProperty("lifecycle"));
            await FormLifecycleContractCapture.WriteAsync(preview, culture, "preview-affected", Ct);
        }
        using var oldSet = await suggestionClient.GetAsync($"/api/forms/definitions/{replaced.Id}/suggestions/revisions/1", Ct);
        Assert.Equal(HttpStatusCode.OK, oldSet.StatusCode);
        Assert.Equal(target.Id, Assert.Single((await oldSet.ReadJsonAsync()).GetProperty("suggestions").EnumerateArray()).GetProperty("destinationDefinitionId").GetGuid());
        using var currentSet = await suggestionClient.GetAsync($"/api/forms/definitions/{replaced.Id}/suggestions", Ct);
        Assert.Empty((await currentSet.ReadJsonAsync()).GetProperty("suggestions").EnumerateArray());
        Assert.Equal(2, (await currentSet.ReadJsonAsync()).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task A_publication_in_progress_rejects_deactivation_without_changing_the_revision_or_active_version()
    {
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, designer, "es");
        using var publisherClient = DeactivationClient(host, publisher, "es");
        var form = await PublishSuggestionFormAsync(host, client, "Publicación pendiente " + Guid.NewGuid().ToString("N")[..8]);
        var attempt = await PublicationAttemptAsync(publisherClient, form.Id, form.Saved);
        using var accepted = await publisherClient.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var rejected = await publisherClient.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(FormPublicationErrors.InProgress.Code, (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        using var detail = await publisherClient.GetAsync($"/api/forms/definitions/{form.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(1, body.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lifecycle").GetProperty("deactivatedAtUtc").ValueKind);
        Assert.False(body.GetProperty("lifecycle").GetProperty("canDeactivate").GetBoolean());
        Assert.Equal("PublicationInProgress", body.GetProperty("lifecycle").GetProperty("unavailableReason").GetString());
        Assert.Equal(form.VersionId, body.GetProperty("publication").GetProperty("activeVersion").GetProperty("versionId").GetGuid());
        Assert.Equal("Accepted", body.GetProperty("publication").GetProperty("currentOperation").GetProperty("state").GetString());
        await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), form.Id);
    }

    private static Guid DeactivationActorId(HttpClient client) =>
        Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());

    private static HttpClient DeactivationClient(WebApplicationFactory<Program> host, HttpClient actor, string culture)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return client;
    }

    private static object DeactivationSuggestion(Guid destinationId, Guid fieldId, bool isActive = true) => new
    {
        id = Guid.NewGuid(), destinationDefinitionId = destinationId, mode = "Always", when = (string?)null, isActive,
        conditions = Array.Empty<object>(), mappings = new[] { new { source = new { kind = "RecordNumber" }, destinationFieldId = fieldId } }
    };

    private static async Task<JsonElement> SaveDeactivationSuggestionsAsync(HttpClient client, Guid definitionId,
        long expectedRevision, params object[] suggestions)
    {
        using var saved = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/suggestions", new { expectedRevision, suggestions }, Ct);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        return await saved.ReadJsonAsync();
    }

    private static async Task<StructuredRecordCaptureResponse> CaptureDeactivationRecordAsync(WebApplicationFactory<Program> host,
        Guid definitionId, Guid versionId, string key, Guid recipientId, string culture)
    {
        using var scope = host.Services.CreateScope();
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), definitionId, versionId, recipientId,
            scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime,
            new Dictionary<string, string> { [key] = "Respuesta de prueba" }) { PresentationCulture = culture };
        var captured = await scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(request, Ct);
        Assert.Equal("Completed", captured.State);
        Assert.NotNull(captured.RecordId);
        return captured;
    }

    private static void AssertDeactivationRecordsHidden(JsonElement lifecycle)
    {
        foreach (var property in new[] { "responseCount", "pendingApprovalCount", "nextRecordNumber" })
            Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty(property).ValueKind);
    }

    private static void AssertDeactivationAudit(JsonElement detail, Guid actorId, DateTime instantUtc)
    {
        var audit = detail.GetProperty("audit");
        Assert.Equal(instantUtc, audit.GetProperty("lastChangedAtUtc").GetDateTime());
        Assert.Equal(actorId, audit.GetProperty("lastChangedBy").GetProperty("userId").GetGuid());
    }
}
