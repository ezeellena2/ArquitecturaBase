using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Loading a response in the app with PostgreSQL: the active version for the person who loads, the paged catalog list with
/// their filters, the multipart send through the common capture, its replay, the «App» origin and the version conflicts.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormEntryHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_entry_shows_the_active_version_with_what_the_person_who_loads_can_choose(string culture)
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client(culture, scenario.FirstId);

        using var entry = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-" + culture, Ct);
        var body = await entry.ReadJsonAsync();
        Assert.Equal(versionId, body.GetProperty("versionId").GetGuid());
        Assert.Equal(culture, body.GetProperty("culture").GetString());
        Assert.Equal(culture == "en" ? "Request a spare part" : "Pedir repuesto", body.GetProperty("name").GetString());
        var question = Assert.Single(body.GetProperty("questions").EnumerateArray());
        Assert.Equal(culture == "en" ? "Supplier" : "Proveedor", question.GetProperty("label").GetString());
        var only = question.GetProperty("catalog").GetProperty("onlyOption");
        Assert.Equal(scenario.FirstItemId, only.GetProperty("id").GetGuid());
        Assert.Equal("FIRST", only.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("description").ValueKind);

        using var options = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions/{scenario.QuestionId}/options?pageSize=50", Ct);
        await FormEntryContractCapture.WriteAsync(options, "options-" + culture, Ct);
        var page = await options.ReadJsonAsync();
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(scenario.FirstItemId, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var searched = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions/{scenario.QuestionId}/options?search=zzz", Ct);
        Assert.Equal(0, (await searched.ReadJsonAsync()).GetProperty("totalCount").GetInt32());

        using var missing = scenario.Client(culture, scenario.MissingId);
        using var blocked = await missing.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        await FormEntryContractCapture.WriteAsync(blocked, "entry-missing-data-" + culture, Ct);
        var catalog = (await blocked.ReadJsonAsync()).GetProperty("questions")[0].GetProperty("catalog");
        Assert.Equal(FormRecordErrors.RecipientDataMissing.Code, catalog.GetProperty("issue").GetString());
        Assert.Equal(culture == "en" ? "Zone" : "Zona", catalog.GetProperty("missingData").GetString());
        using var missingOptions = await missing.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions/{scenario.QuestionId}/options", Ct);
        Assert.Equal(HttpStatusCode.Conflict, missingOptions.StatusCode);

        using var manager = scenario.Client(culture);
        using var forbidden = await manager.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = scenario.Host.CreateClient();
        using var unauthorized = await anonymous.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var unknown = await first.GetAsync($"/api/forms/entries/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_send_is_saved_in_the_name_of_who_loads_it_with_the_app_origin_and_its_replay_keeps_the_number(string culture)
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var readerId = await AddAccountAsync(scenario, [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate], "Laura Ríos");
        using var first = scenario.Client(culture, scenario.FirstId);
        var requestId = Guid.NewGuid();

        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(requestId, versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        await FormEntryContractCapture.WriteAsync(sent, "submitted-" + culture, Ct);
        var saved = await sent.ReadJsonAsync();
        var prefix = (await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormDefinition>()
            .Where(definition => definition.Id == scenario.DefinitionId).Select(definition => definition.Prefix).SingleAsync(Ct)));
        Assert.Equal(prefix + "-000001", saved.GetProperty("number").GetString());
        Assert.Equal("Received", saved.GetProperty("state").GetString());
        using var replayed = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(requestId, versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(saved.GetRawText(), (await replayed.ReadJsonAsync()).GetRawText());

        var recordId = saved.GetProperty("recordId").GetGuid();
        var record = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecord>().SingleAsync(value => value.Id == recordId, Ct));
        Assert.True(record.EnteredInApp);
        Assert.Equal(scenario.FirstId, record.ActorUserId);
        Assert.Equal(culture, record.PresentationCulture);
        var references = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordReference>()
            .Where(value => value.RecordId == recordId).Select(value => value.ItemId).ToArrayAsync(Ct));
        Assert.Equal([scenario.FirstItemId], references);
        Assert.Equal(1, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecord>().CountAsync(value => value.DefinitionId == scenario.DefinitionId, Ct)));

        using var reader = scenario.Client(culture, readerId);
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{scenario.DefinitionId}/records", Ct);
        await FormEntryContractCapture.WriteAsync(sheet, "sheet-app-" + culture, Ct);
        var row = (await sheet.ReadJsonAsync()).GetProperty("records").GetProperty("items")[0];
        Assert.Equal(FormEntryChannel.Id, row.GetProperty("channel").GetString());
        Assert.Equal("Juan Gómez", row.GetProperty("actor").GetProperty("displayName").GetString());
        using var app = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/search", JsonContent.Create(new { enteredInApp = true }), Ct);
        Assert.Equal(1, (await app.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var channel = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/search", JsonContent.Create(new { enteredInApp = false }), Ct);
        Assert.Equal(0, (await channel.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var counted = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/filter-counts/search",
            JsonContent.Create(new { filter = new { enteredInApp = false }, dimension = "version" }), Ct);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        using var detail = await reader.GetAsync($"/api/forms/records/{recordId}", Ct);
        await FormEntryContractCapture.WriteAsync(detail, "detail-app-" + culture, Ct);
        var detailBody = await detail.ReadJsonAsync();
        Assert.Equal(FormEntryChannel.Id, detailBody.GetProperty("origin").GetProperty("channelId").GetString());
        Assert.Equal("Juan Gómez", detailBody.GetProperty("origin").GetProperty("senderName").GetString());
        Assert.DoesNotContain(detailBody.GetProperty("history").EnumerateArray(), moment => moment.GetProperty("kind").GetString() == "Sent");

        using var overview = await first.GetAsync("/api/forms/overview", Ct);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        await FormEntryContractCapture.WriteAsync(overview, "overview-create-" + culture, Ct);
        var form = Assert.Single((await overview.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray(),
            value => value.GetProperty("id").GetGuid() == scenario.DefinitionId);
        // Whoever only loads counts their own responses and sees their last one (CW6-C).
        Assert.Equal(1, form.GetProperty("responseCount").GetInt64());
        Assert.Equal(recordId, form.GetProperty("lastResponse").GetProperty("id").GetGuid());
        Assert.Equal(prefix + "-000001", form.GetProperty("lastResponse").GetProperty("number").GetString());
        using var second = scenario.Client(culture, scenario.SecondId);
        using var nothingYet = await second.GetAsync("/api/forms/overview", Ct);
        var empty = Assert.Single((await nothingYet.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray(),
            value => value.GetProperty("id").GetGuid() == scenario.DefinitionId);
        Assert.Equal(0, empty.GetProperty("responseCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastResponse").ValueKind);
    }

    [Fact]
    public async Task Whoever_only_loads_reads_their_own_responses_and_nothing_of_anyone_else()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        using var second = scenario.Client("es", scenario.SecondId);
        using var sentByFirst = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        var own = (await sentByFirst.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        using var sentBySecond = await second.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.SecondItemId)), Ct);
        var other = (await sentBySecond.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var records = $"/api/forms/definitions/{scenario.DefinitionId}/records";
        static Guid[] Ids(JsonElement sheet) => sheet.GetProperty("records").GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray();

        using var sheet = await first.GetAsync(records, Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        Assert.Equal([own], Ids(await sheet.ReadJsonAsync()));
        using var searched = await first.PostAsync(records + "/search", JsonContent.Create(new { actorIds = new[] { scenario.SecondId } }), Ct);
        Assert.Empty(Ids(await searched.ReadJsonAsync()));
        using var counted = await first.PostAsync(records + "/filter-counts/search", JsonContent.Create(new { filter = new { }, dimension = "version" }), Ct);
        Assert.Equal(1, (await counted.ReadJsonAsync()).GetProperty("items")[0].GetProperty("count").GetInt32());
        // The options of a count come only from their own responses: neither someone else's name nor the option they chose.
        using var byPerson = await first.PostAsync(records + "/filter-counts/search", JsonContent.Create(new { filter = new { }, dimension = "actor" }), Ct);
        Assert.Equal([scenario.FirstId.ToString("D")], (await byPerson.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        using var byOption = await first.PostAsync(records + "/filter-counts/search", JsonContent.Create(new { filter = new { }, dimension = "choice", fieldId = scenario.QuestionId }), Ct);
        Assert.Equal([scenario.FirstItemId.ToString("D")], (await byOption.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        using var navigation = await first.PostAsync($"{records}/{own}/navigation", JsonContent.Create(new { }), Ct);
        Assert.Equal(1, (await navigation.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var detail = await first.GetAsync($"/api/forms/records/{own}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var workflow = await first.GetAsync($"/api/forms/records/{own}/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        Assert.Empty((await workflow.ReadJsonAsync()).GetProperty("actions").EnumerateArray());

        // Someone else's response does not exist for them, not even by its address.
        foreach (var path in new[] { $"/api/forms/records/{other}", $"/api/forms/records/{other}/workflow",
            $"/api/forms/records/{other}/files/{Guid.NewGuid()}/content", $"/api/forms/records/{other}/documents/{Guid.NewGuid()}/content" })
        {
            using var foreign = await first.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using var foreignNavigation = await first.PostAsync($"{records}/{other}/navigation", JsonContent.Create(new { }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignNavigation.StatusCode);
        using var export = await first.PostAsync(records + "/export", JsonContent.Create(new { filter = new { } }), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        using var manager = scenario.Client("es");
        using var withoutLoading = await manager.GetAsync(records, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, withoutLoading.StatusCode);
    }

    [Fact]
    public async Task An_option_outside_the_person_filters_is_marked_on_its_question_without_a_number()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);

        using var rejected = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.SecondItemId)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await FormEntryContractCapture.WriteAsync(rejected, "option-unavailable-es", Ct);
        var problem = await rejected.ReadJsonAsync();
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("answers." + scenario.QuestionId.ToString("D"), out _));
        Assert.Equal(0, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormDefinition>()
            .Where(definition => definition.Id == scenario.DefinitionId).Select(definition => definition.LastRecordNumber).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_new_version_or_a_deactivation_is_a_conflict_while_a_repeated_send_keeps_its_result()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        var requestId = Guid.NewGuid();
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(requestId, versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        var number = (await sent.ReadJsonAsync()).GetProperty("number").GetString();

        await PublishNextVersionAsync(scenario, versionId);
        using var stale = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await FormEntryContractCapture.WriteAsync(stale, "version-changed-es", Ct);
        Assert.Equal(FormEntryErrors.VersionChanged.Code, (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        using var staleOptions = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions/{scenario.QuestionId}/options", Ct);
        Assert.Equal(HttpStatusCode.Conflict, staleOptions.StatusCode);
        using var repeated = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(requestId, versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(number, (await repeated.ReadJsonAsync()).GetProperty("number").GetString());

        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
            Assert.True(definition.Deactivate(definition.Revision, scenario.ManagerId, factory.Clock.GetUtcNow().UtcDateTime).IsSuccess);
            return await db.SaveChangesAsync(Ct);
        });
        using var deactivated = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);
        await FormEntryContractCapture.WriteAsync(deactivated, "unavailable-es", Ct);
        Assert.Equal(FormEntryErrors.Unavailable.Code, (await deactivated.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Photos_travel_in_the_same_send_and_a_file_that_does_not_fit_is_marked_on_its_question()
    {
        await using var emulator = new AzuriteFixture();
        await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions
        {
            Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString,
        }));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(store);
        }));
        var userId = await factory.InTransactionAsync(async services =>
        {
            var role = "entry-photos-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, [BusinessFormsPermissions.RecordsCreate], Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var account = await users.CreateAsync(Email.Create(TestEmails.Unique("entry-photo")).Value, null, false, "Marcos Gil", "es", Ct);
            await users.SetRolesAsync(account.Id, [role], Ct);
            return account.Id;
        });
        var (definitionId, versionId, textId, photoId) = await SeedPhotoFormAsync();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString("D"));
        var image = new FormPdfRendererFixture().PhotoContent;

        using var sent = await client.PostAsync($"/api/forms/entries/{definitionId}",
            Send(Guid.NewGuid(), versionId, Text(textId, "Lote 7"), (photoId, "lote.jpg", "image/jpeg", image)), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var files = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordFile>().Where(file => file.RecordId == recordId)
            .Join(db.Set<StoredFile>(), file => file.FileId, stored => stored.Id, (file, stored) => new { file.FieldId, stored.Name, stored.Size }).ToArrayAsync(Ct));
        var saved = Assert.Single(files);
        Assert.Equal(photoId, saved.FieldId);
        Assert.Equal("lote.jpg", saved.Name);
        Assert.Equal(image.LongLength, saved.Size);

        using var rejected = await client.PostAsync($"/api/forms/entries/{definitionId}",
            Send(Guid.NewGuid(), versionId, Text(textId, "Lote 8"), (photoId, "lote.jpg", "image/jpeg", "not a photo"u8.ToArray())), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.ReadJsonAsync();
        Assert.Equal(FileErrors.InvalidType.Code, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("answers." + photoId.ToString("D"), out _));
        using var withoutQuestion = await client.PostAsync($"/api/forms/entries/{definitionId}", Unpaired(versionId, image), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, withoutQuestion.StatusCode);
    }

    [Fact]
    public async Task Notifying_the_author_of_an_app_entry_without_a_linked_channel_does_not_apply()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == versionId, Ct);
            var notice = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { ["es"] = "Recibimos {número}", ["en"] = "Received {number}" }, false);
            var rules = BusinessFormRuleSet.Create(definition.Id, 1, "es", JsonSerializer.Serialize(new[] { new FormRule(Guid.NewGuid(), "Received", [], [notice]) }, JsonSerializerOptions.Web),
                false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(rules);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(rules.Id);
            return await db.SaveChangesAsync(Ct);
        });
        using var first = scenario.Client("es", scenario.FirstId);

        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var run = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormActionRun>().SingleAsync(value => value.RecordId == recordId, Ct));
        Assert.Equal("NotifyAuthor", run.Kind);
        Assert.Equal(FormActionRunState.NotApplicable, run.State);
    }

    private static object Choice(Guid fieldId, Guid itemId) => new { fieldId, value = itemId.ToString("D") };
    private static object Text(Guid fieldId, string value) => new { fieldId, value };

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, params object[] answers) => Send(requestId, versionId, answers, []);

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, object answer, (Guid FieldId, string Name, string ContentType, byte[] Content) file) =>
        Send(requestId, versionId, [answer], [file]);

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, object[] answers, (Guid FieldId, string Name, string ContentType, byte[] Content)[] files)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(requestId.ToString("D")), "requestId" },
            { new StringContent(versionId.ToString("D")), "versionId" },
            { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
        };
        foreach (var file in files)
        {
            var bytes = new ByteArrayContent(file.Content);
            bytes.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(bytes, "files", file.Name);
            content.Add(new StringContent(file.FieldId.ToString("D")), "fileFieldIds");
        }
        return content;
    }

    // A file without the question it answers is not paired: the validator rejects it before anything is stored.
    private static MultipartFormDataContent Unpaired(Guid versionId, byte[] image)
    {
        var content = Send(Guid.NewGuid(), versionId, []);
        var bytes = new ByteArrayContent(image);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(bytes, "files", "suelta.jpg");
        return content;
    }

    private async Task PublishNextVersionAsync(FormRecipientScenario scenario, Guid activeVersionId) => await scenario.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
        var active = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == activeVersionId, Ct);
        var next = BusinessFormVersion.Create(definition.Id, 2, new string('b', 64));
        next.SealSchema(active.SchemaJson);
        next.SealPresentation(active.NameJson!, active.SubmitLabelJson!, active.Culture!, active.QuestionCount!.Value);
        db.Set<BusinessFormVersion>().Add(next);
        await db.SaveChangesAsync(Ct);
        db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, next, scenario.ManagerId, ["es", "en"]));
        await db.SaveChangesAsync(Ct);
        Assert.True(definition.ActivateVersion(activeVersionId, next.Id).IsSuccess);
        return await db.SaveChangesAsync(Ct);
    });

    private static async Task<Guid> AddAccountAsync(FormRecipientScenario scenario, string[] permissions, string name) => await scenario.ScopeAsync(async services =>
    {
        Guid id = default;
        var saved = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var role = "entry-reader-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, token);
            var users = services.GetRequiredService<IUserRepository>();
            var account = await users.CreateAsync(Email.Create(TestEmails.Unique("entry")).Value, null, false, name, "es", token);
            await users.SetRolesAsync(account.Id, [role], token);
            id = account.Id;
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(saved.IsSuccess);
        return id;
    });

    private async Task<(Guid DefinitionId, Guid VersionId, Guid TextId, Guid PhotoId)> SeedPhotoFormAsync()
    {
        var textId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var labels = new Dictionary<string, string> { ["es"] = "Lote", ["en"] = "Field" };
        var fields = new[]
        {
            new CanonicalFormField(textId, "field_" + textId.ToString("N"), "shortText", labels, null, true, 80, null),
            new CanonicalFormField(photoId, "field_" + photoId.ToString("N"), "photo", new Dictionary<string, string> { ["es"] = "Fotos del lote", ["en"] = "Field photos" },
                null, true, null, null, Files: new(1, 3, null)),
        };
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, new string('c', 64));
        Assert.True(definition.SetDraft(1, "Siembra", "{\"es\":\"Siembra\",\"en\":\"Sowing\"}", "S" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, fields), JsonSerializerOptions.Web), fields.Length, creating: true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web));
        version.SealPresentation("{\"es\":\"Siembra\",\"en\":\"Sowing\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", fields.Length);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, Guid.NewGuid(), ["es", "en"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, version.Id, textId, photoId);
    }
}
