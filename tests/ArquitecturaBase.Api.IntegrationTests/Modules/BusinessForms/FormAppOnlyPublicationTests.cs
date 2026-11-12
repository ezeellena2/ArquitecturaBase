using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
// SA1-A to SA5 (2026-11-09): a form completed only in the app publishes at once, without asking a channel, and the
// channels do not offer it. What existed keeps its exact responses: the new values are omitted unless they are true.
// Its intact bodies go to forms-http/app-only for the front; the check is not exported because it carries a ticket.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_form_completed_only_in_the_app_publishes_at_once_and_the_channels_do_not_offer_it()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var form = Payload("App only " + Guid.NewGuid().ToString("N")[..10]); form["draft"]!["appOnly"] = true;
        using var created = await client.PostAsJsonAsync("/api/forms/definitions", form, Ct);
        var saved = await CaptureContractAsync(created, HttpStatusCode.Created, "app-only", "created");
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        Assert.True(saved.GetProperty("definition").GetProperty("draft").GetProperty("appOnly").GetBoolean());

        // The channel of the tests cannot publish (no account): only in the app it does not matter.
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedResult = await check.ReadJsonAsync();
        Assert.Equal("LocallyValidated", checkedResult.GetProperty("compatibility").GetString());
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Equal("Available", checkedResult.GetProperty("acceptanceAvailability").GetString());
        Assert.Empty(checkedResult.GetProperty("channels").EnumerateArray());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());

        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        var receipt = await CaptureContractAsync(accepted, HttpStatusCode.Accepted, "app-only", "accepted");
        Assert.Equal("Published", receipt.GetProperty("state").GetString());
        Assert.Equal(1, receipt.GetProperty("versionNumber").GetInt64());
        using var replay = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(receipt.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());

        using var status = await client.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
        var state = await CaptureContractAsync(status, HttpStatusCode.OK, "app-only", "status-es");
        Assert.Equal("Published", state.GetProperty("state").GetString());
        Assert.Equal(["en", "es"], state.GetProperty("cultures").EnumerateArray().Select(value => value.GetString()!).Order().ToArray());

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await CaptureContractAsync(detail, HttpStatusCode.OK, "app-only", "detail-es");
        var publication = loaded.GetProperty("publication");
        Assert.Equal("Published", publication.GetProperty("displayState").GetString());
        Assert.Equal(1, publication.GetProperty("activeVersion").GetProperty("versionNumber").GetInt64());
        Assert.True(publication.GetProperty("activeVersion").GetProperty("appOnly").GetBoolean());
        Assert.Equal(["en", "es"], publication.GetProperty("activeVersion").GetProperty("cultures").EnumerateArray()
            .Select(value => value.GetString()!).Order().ToArray());
        Assert.False(loaded.GetProperty("capabilities").GetProperty("canPreview").GetBoolean());
        Assert.True(loaded.GetProperty("lifecycle").GetProperty("publishedOnlyInApp").GetBoolean());

        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().AsNoTracking().SingleAsync(x => x.Id == id, Ct));
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().AsNoTracking().SingleAsync(x => x.DefinitionId == id, Ct));
        var operation = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().AsNoTracking().SingleAsync(x => x.DefinitionId == id, Ct));
        Assert.Equal(version.Id, definition.ActiveVersionId);
        Assert.True(definition.PrefixLocked);
        Assert.True(version.AppOnly);
        Assert.Equal(["en", "es"], version.PublishedCultures!.Order().ToArray());
        Assert.Equal(FormPublicationState.Published, operation.State);
        Assert.False(operation.IsOpen);

        using var list = await client.GetAsync($"/api/forms/definitions?search={Uri.EscapeDataString(saved.GetProperty("definition").GetProperty("name").GetProperty("es").GetString()!)}", Ct);
        var row = Assert.Single((await CaptureContractAsync(list, HttpStatusCode.OK, "app-only", "list-es")).GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("appOnly").GetBoolean());

        var offered = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IFormSelectionReader>();
            return (await reader.FindAsync(id, version.Id, Ct), await reader.ListSuggestionTargetsAsync([id], Ct));
        });
        Assert.Null(offered.Item1);
        Assert.Empty(offered.Item2);
    }

    // SA4-A: a form that another one suggests and that moves to the app is no longer offered by WhatsApp. The suggestion stays,
    // unavailable with its own state, until it is removed; its options go intact to forms-http/app-only for the front.
    [Fact]
    public async Task A_suggestion_to_a_form_that_moved_to_the_app_stays_unavailable_with_its_state()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var name = "Moved to the app " + Guid.NewGuid().ToString("N")[..8];
        var target = await PublishSuggestionFormAsync(host, client, name + " destino");
        var source = await PublishSuggestionFormAsync(host, client, name + " origen");
        using var manager = await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var field = target.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();
        await SaveDeactivationSuggestionsAsync(manager, source.Id, 0, DeactivationSuggestion(target.Id, field));

        var moved = EditPayload(target.Saved); moved["draft"]!["appOnly"] = true;
        using var saved = await client.PutAsJsonAsync($"/api/forms/definitions/{target.Id}/draft", moved, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var current = await saved.ReadJsonAsync();
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{target.Id}/publication-checks", CheckPayload(current), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        var attempt = CheckPayload(current); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{target.Id}/publications", attempt, Ct);
        Assert.Equal("Published", (await accepted.ReadJsonAsync()).GetProperty("state").GetString());

        using var options = await manager.GetAsync($"/api/forms/definitions/{source.Id}/suggestions/options", Ct);
        var body = await CaptureContractAsync(options, HttpStatusCode.OK, "app-only", "suggestion-options-es");
        Assert.DoesNotContain(body.GetProperty("destinations").EnumerateArray(), row => row.GetProperty("definitionId").GetGuid() == target.Id);
        var unavailable = Assert.Single(body.GetProperty("unavailableDestinations").EnumerateArray());
        Assert.Equal(target.Id, unavailable.GetProperty("definitionId").GetGuid());
        Assert.Equal("AppOnly", unavailable.GetProperty("state").GetString());
        Assert.Equal(name + " destino", unavailable.GetProperty("name").GetProperty("es").GetString());
    }

    // A version published only in the app seals its languages without a channel profile. Its responses read like any other in
    // the sheet, the detail and an approval; the reader of versions took them from the profiles and gave 500 (2026-11-12).
    [Fact]
    public async Task The_responses_of_a_version_published_only_in_the_app_read_in_the_sheet_the_detail_and_an_approval()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        decider.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var form = Payload("App only responses " + Guid.NewGuid().ToString("N")[..10]); form["draft"]!["appOnly"] = true;
        var saved = await CreateAsync(author, form);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var fieldId = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = (await check.ReadJsonAsync()).GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        var receipt = await accepted.ReadJsonAsync();
        Assert.Equal("Published", receipt.GetProperty("state").GetString());
        var versionId = receipt.GetProperty("versionId").GetGuid();

        using var sent = await author.PostAsync($"/api/forms/entries/{id}", new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
            { new StringContent(versionId.ToString("D")), "versionId" },
            { new StringContent(JsonSerializer.Serialize(new[] { new { fieldId, value = "Ana Ruiz" } }, JsonSerializerOptions.Web)), "answers" },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();

        using var sheet = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/records/search", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        var rows = await sheet.ReadJsonAsync();
        var row = Assert.Single(rows.GetProperty("records").GetProperty("items").EnumerateArray());
        Assert.Equal(recordId, row.GetProperty("id").GetGuid());
        Assert.Equal(versionId, Assert.Single(rows.GetProperty("versions").EnumerateArray()).GetProperty("id").GetGuid());
        using var detail = await author.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        // The approval of that response, opened as the workflow opens it: a rule set, its run and the task for the decider's role.
        var taskId = await factory.ExecuteDbContextAsync(async db =>
        {
            var deciderId = Guid.Parse(decider.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
            var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
            var roleId = await db.UserRoles.Where(link => link.UserId == deciderId).Select(link => link.RoleId).SingleAsync(Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == versionId, Ct);
            var approval = new FormRuleAction(Guid.NewGuid(), "RequestApproval", roleId, null, false);
            var received = new FormRule(Guid.NewGuid(), "Received", [], [approval]);
            var ruleSet = BusinessFormRuleSet.Create(id, 1, version.Culture!, FormDraftPolicy.Serialize(new[] { received }), true, version.SchemaJson);
            var at = factory.Clock.GetUtcNow().UtcDateTime;
            var run = BusinessFormActionRun.Create(id, recordId, ruleSet.Id, received.Id, approval.Id, "Received", "RequestApproval", 0, null,
                FormDraftPolicy.Serialize(new FormActionSnapshot(roleId, null, null, [])), true, at);
            var task = BusinessFormTask.Open(id, recordId, ruleSet.Id, run.Id, roleId, authorId, at);
            db.Set<BusinessFormRuleSet>().Add(ruleSet); db.Set<BusinessFormActionRun>().Add(run); db.Set<BusinessFormTask>().Add(task);
            await db.SaveChangesAsync(Ct);
            return task.Id;
        });
        using var opened = await decider.GetAsync($"/api/forms/approvals/{taskId}", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(recordId, (await opened.ReadJsonAsync()).GetProperty("recordId").GetGuid());
        using var inbox = await decider.GetAsync($"/api/forms/approvals?definitionId={id}", Ct);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.Equal(taskId, Assert.Single((await inbox.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_form_completed_also_by_a_channel_keeps_every_response_as_it_was()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(client, Payload("Channel " + Guid.NewGuid().ToString("N")[..10]));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        Assert.False(saved.GetProperty("definition").GetProperty("draft").TryGetProperty("appOnly", out _));

        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.False(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Equal("Unavailable", checkedResult.GetProperty("acceptanceAvailability").GetString());
        Assert.NotEmpty(checkedResult.GetProperty("channels").EnumerateArray());

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await detail.ReadJsonAsync();
        Assert.False(loaded.GetProperty("lifecycle").TryGetProperty("publishedOnlyInApp", out _));
        using var list = await client.GetAsync($"/api/forms/definitions?search={Uri.EscapeDataString(saved.GetProperty("definition").GetProperty("name").GetProperty("es").GetString()!)}", Ct);
        Assert.False(Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray()).TryGetProperty("appOnly", out _));
    }

    [Fact]
    public async Task Without_a_form_channel_every_form_publishes_only_in_the_app()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(writer, Payload("No channel " + Guid.NewGuid().ToString("N")[..10]));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.RemoveAll<IStructuredFormChannel>()));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, writer.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));

        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Empty(checkedResult.GetProperty("channels").EnumerateArray());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("Published", (await accepted.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().AnyAsync(x => x.DefinitionId == id && x.AppOnly, Ct)));
        using var presentation = await client.GetAsync("/api/settings/presentation", Ct);
        Assert.DoesNotContain("form-channels", (await presentation.ReadJsonAsync()).GetProperty("installedFeatures").EnumerateArray().Select(value => value.GetString()));
    }
}
