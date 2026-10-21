using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Publication_check_uses_saved_identity_without_writes_and_keeps_the_closed_detail_contract()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var saved = await CreateAsync(client, Payload("Local compiler"));
        var definition = saved.GetProperty("definition"); var id = definition.GetProperty("id").GetGuid();
        var request = CheckPayload(saved);
        var before = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().CountAsync(x => x.DefinitionId == id, Ct));
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var result = await check.ReadJsonAsync();
        Assert.Equal(1, result.GetProperty("draftRevision").GetInt64());
        Assert.Equal("LocallyValidated", result.GetProperty("compatibility").GetString());
        Assert.False(result.GetProperty("canAccept").GetBoolean());
        Assert.Equal("Unavailable", result.GetProperty("acceptanceAvailability").GetString());
        Assert.Equal("Pending", result.GetProperty("channels")[0].GetProperty("externalEvidence").GetString());
        Assert.Equal(2, result.GetProperty("channels")[0].GetProperty("cultures").GetArrayLength());
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
        using var repeated = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(result.GetRawText(), (await repeated.ReadJsonAsync()).GetRawText());
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await detail.ReadJsonAsync();
        Assert.Equal(new[] { "definition", "capabilities", "audit", "publication", "lifecycle" }, loaded.EnumerateObject().Select(x => x.Name).ToArray());
        var lifecycle = loaded.GetProperty("lifecycle");
        Assert.Equal(definition.GetProperty("revision").GetInt64(), lifecycle.GetProperty("revision").GetInt64());
        Assert.True(lifecycle.GetProperty("canDelete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("responseCount").ValueKind);
        Assert.False(loaded.GetProperty("definition").TryGetProperty("lifecycle", out _));
        foreach (var property in lifecycle.EnumerateObject().Where(property => property.Name != "revision"))
            Assert.False(loaded.GetProperty("definition").TryGetProperty(property.Name, out _));
        Assert.Equal(definition.GetRawText(), loaded.GetProperty("definition").GetRawText());
        Assert.Equal(before, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().CountAsync(x => x.DefinitionId == id, Ct)));
    }
    [Fact]
    public async Task Publication_check_rejects_unsaved_content_new_ids_and_stale_revision()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var saved = await CreateAsync(client, Payload());
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var request = CheckPayload(saved); request["name"] = new JsonObject { ["es"] = "Unsaved", ["en"] = "Unsaved" };
        using var changed = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Forms.Publication.DraftMustBeSaved", (await changed.ReadJsonAsync()).GetProperty("code").GetString());
        request = CheckPayload(saved); request["draft"]!["fields"]![0]!.AsObject().Remove("fieldId");
        using var newId = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, newId.StatusCode);
        request = CheckPayload(saved); request["expectedRevision"] = 2;
        using var stale = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.Draft.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        request = CheckPayload(saved); request["unexpected"] = true;
        using var invalid = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missing = await client.PostAsJsonAsync($"/api/forms/definitions/{Guid.NewGuid()}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
    [Fact]
    public async Task Saved_incompatible_label_returns_localized_issue_with_exact_identity_and_does_not_clip()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var payload = Payload();
        payload["draft"]!["fields"]![0]!["label"]!["es"] = new string('x', 21);
        var saved = await CreateAsync(client, payload); var definition = saved.GetProperty("definition"); var id = definition.GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadJsonAsync(); Assert.Equal("Blocked", result.GetProperty("compatibility").GetString());
        var issue = result.GetProperty("issues")[0];
        Assert.Equal(definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(), issue.GetProperty("fieldId").GetGuid());
        Assert.Equal("WhatsApp.Flow.TextTooLong", issue.GetProperty("code").GetString());
        Assert.Equal("draft.fields[0].label.es", issue.GetProperty("path").GetString());
        Assert.Equal(20, issue.GetProperty("limit").GetInt32());
        Assert.Contains("limit", issue.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_saved_form_exceeding_the_response_budget_returns_localized_field_issues_without_acceptance_ticket()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Response budget");
        var fields = new JsonArray();
        for (var index = 0; index < 40; index++)
        {
            fields.Add(new JsonObject
            {
                ["localId"] = $"response-{index}", ["type"] = "longText", ["required"] = true, ["maxLength"] = 10000,
                ["label"] = new JsonObject { ["es"] = $"Respuesta {index}", ["en"] = $"Response {index}" },
            });
        }
        payload["draft"] = new JsonObject
        {
            ["format"] = 1, ["culture"] = "es",
            ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = fields,
        };
        var saved = await CreateAsync(client, payload);
        var definition = saved.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var expectedFieldId = definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();

        foreach (var (culture, expectedMessage) in new[]
        {
            ("es", "La respuesta máxima posible del formulario supera el límite seguro de WhatsApp."),
            ("en", "The largest possible form response exceeds WhatsApp's safe response limit."),
        })
        {
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync(Ct);
            using var parsed = JsonDocument.Parse(body);
            var result = parsed.RootElement;
            Assert.Equal("Blocked", result.GetProperty("compatibility").GetString());
            Assert.False(result.GetProperty("canAccept").GetBoolean());
            Assert.True(!result.TryGetProperty("checkToken", out var token) || token.ValueKind == JsonValueKind.Null);
            Assert.Equal("Pending", result.GetProperty("channels")[0].GetProperty("externalEvidence").GetString());
            var issue = Assert.Single(result.GetProperty("issues").EnumerateArray(), item =>
                item.GetProperty("code").GetString() == "WhatsApp.Flow.ResponseTooLarge"
                && item.GetProperty("fieldId").GetGuid() == expectedFieldId
                && item.GetProperty("path").GetString() == "draft.fields[0].maxLength"
                && item.GetProperty("culture").GetString() == "en");
            Assert.Contains(expectedMessage, issue.GetProperty("message").GetString(), StringComparison.Ordinal);

            var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, "budget-http");
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, culture + ".json"), body, Ct);
            }
        }
    }
    [Fact]
    public async Task Missing_structured_channel_is_explicit_and_check_requires_a_session()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var saved = await CreateAsync(client, Payload()); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        using var anonymous = factory.CreateClient();
        using var rejected = await anonymous.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.RemoveAll<IStructuredFormChannel>()));
        using var withoutChannel = host.CreateClient();
        withoutChannel.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var unavailable = await withoutChannel.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        var result = await unavailable.ReadJsonAsync();
        Assert.Equal("Unavailable", result.GetProperty("compatibility").GetString());
        Assert.Empty(result.GetProperty("channels").EnumerateArray());
        Assert.Equal("Forms.Publication.ChannelUnavailable", result.GetProperty("issues")[0].GetProperty("code").GetString());
    }
    [Fact]
    public async Task Publication_check_requires_read_and_publish_but_does_not_require_manage()
    {
        using var admin = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var saved = await CreateAsync(admin, Payload()); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        foreach (var codes in new[] { new[] { BusinessFormsPermissions.Read }, new[] { BusinessFormsPermissions.Publish }, new[] { BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage } })
        {
            using var denied = await OperatorAsync(codes);
            using var response = await denied.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var allowed = await publisher.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Catalog_choice_keeps_its_catalog_source_and_publication_check_reports_an_invalid_catalog()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var catalogId = Guid.NewGuid();
        var payload = Payload("Catalog choice");
        payload["draft"] = new JsonObject
        {
            ["format"] = 1,
            ["culture"] = "es",
            ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(new JsonObject
            {
                ["localId"] = "catalog-choice",
                ["type"] = "choice",
                ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Responsable", ["en"] = "Assignee" },
                ["source"] = new JsonObject { ["catalog"] = catalogId },
            }),
        };

        using var createdForm = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        var saved = await CaptureK6FixtureAsync(createdForm, "catalog-choice-invalid-created", HttpStatusCode.Created);
        var definition = saved.GetProperty("definition");
        var field = definition.GetProperty("draft").GetProperty("fields")[0];
        Assert.Equal("choice", field.GetProperty("type").GetString());
        Assert.Equal(catalogId, field.GetProperty("source").GetProperty("catalog").GetGuid());
        Assert.Equal(JsonValueKind.Null, field.GetProperty("options").ValueKind);

        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{definition.GetProperty("id").GetGuid()}/publication-checks", CheckPayload(saved), Ct);
        var result = await CaptureK6FixtureAsync(check, "catalog-choice-check-invalid", HttpStatusCode.OK);
        Assert.Equal("Blocked", result.GetProperty("compatibility").GetString());
        Assert.False(result.GetProperty("canAccept").GetBoolean());
        Assert.Empty(result.GetProperty("channels").EnumerateArray());
        Assert.True(!result.TryGetProperty("checkToken", out var token) || token.ValueKind == JsonValueKind.Null);
        var issue = Assert.Single(result.GetProperty("issues").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == "draft.fields[0].source.catalog");
        Assert.Equal("Forms.Publication.CatalogInvalid", issue.GetProperty("code").GetString());
        Assert.Equal(field.GetProperty("fieldId").GetGuid(), issue.GetProperty("fieldId").GetGuid());
    }

    [Fact]
    public async Task Active_catalog_choice_checks_without_catalog_permission_and_counts_distinct_current_definitions()
    {
        var usageSearch = "K6" + Guid.NewGuid().ToString("N")[..8];
        using var catalogManager = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.Read]);
        using var createdCatalog = await catalogManager.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Personas activas " + usageSearch, en = "Active people " + usageSearch },
            description = new { es = "Lista para elegir", en = "Choice list" },
            items = new[]
            {
                new { localId = "active", itemId = (Guid?)null, code = "ACTIVE", text = new { es = "Activa", en = "Active" }, detail = new { es = (string?)null, en = (string?)null }, isActive = true },
            },
        }, Ct);
        var catalog = await CaptureK6FixtureAsync(createdCatalog, "active-catalog-created", HttpStatusCode.Created);
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();

        using var unusedCatalog = await catalogManager.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Lista sin uso " + usageSearch, en = "Unused list " + usageSearch },
            description = new { es = "Lista para ordenar", en = "List for sorting" },
            items = new[]
            {
                new { localId = "other", itemId = (Guid?)null, code = "OTHER", text = new { es = "Otra", en = "Other" }, detail = new { es = (string?)null, en = (string?)null }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, unusedCatalog.StatusCode);
        var unusedCatalogId = (await unusedCatalog.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();

        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Valid catalog choice");
        payload["draft"] = new JsonObject
        {
            ["format"] = 1,
            ["culture"] = "es",
            ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(
                new JsonObject
                {
                    ["localId"] = "catalog-choice",
                    ["type"] = "choice",
                    ["required"] = true,
                    ["label"] = new JsonObject { ["es"] = "Responsable", ["en"] = "Assignee" },
                    ["source"] = new JsonObject { ["catalog"] = catalogId },
                },
                new JsonObject
                {
                    ["localId"] = "catalog-choice-copy",
                    ["type"] = "choice",
                    ["required"] = false,
                    ["label"] = new JsonObject { ["es"] = "Otra persona", ["en"] = "Another person" },
                    ["source"] = new JsonObject { ["catalog"] = catalogId },
                }),
        };
        using var createdForm = await publisher.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        var saved = await CaptureK6FixtureAsync(createdForm, "catalog-choice-created", HttpStatusCode.Created);
        var definition = saved.GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var createRequestId = saved.GetProperty("requestId").GetGuid();
        using var createReceipt = await publisher.GetAsync($"/api/forms/draft-changes/{createRequestId}", Ct);
        await CaptureK6FixtureAsync(createReceipt, "catalog-choice-create-receipt", HttpStatusCode.OK);

        using var detail = await publisher.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        await CaptureK6FixtureAsync(detail, "catalog-choice-detail", HttpStatusCode.OK);

        var update = EditPayload(saved);
        update["draft"]!["fields"]![0]!["label"]!["es"] = "Responsable equipo";
        update["draft"]!["fields"]![0]!["label"]!["en"] = "Team assignee";
        using var updatedForm = await publisher.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", update, Ct);
        var updated = await CaptureK6FixtureAsync(updatedForm, "catalog-choice-updated", HttpStatusCode.OK);
        var updateRequestId = updated.GetProperty("requestId").GetGuid();
        using var updateReceipt = await publisher.GetAsync($"/api/forms/draft-changes/{updateRequestId}", Ct);
        await CaptureK6FixtureAsync(updateReceipt, "catalog-choice-update-receipt", HttpStatusCode.OK);

        using var reloaded = await publisher.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        await CaptureK6FixtureAsync(reloaded, "catalog-choice-reloaded", HttpStatusCode.OK);

        using var check = await publisher.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", CheckPayload(updated), Ct);
        var validCheck = await CaptureK6FixtureAsync(check, "catalog-choice-check-valid", HttpStatusCode.OK);
        var result = validCheck;
        Assert.Equal("LocallyValidated", result.GetProperty("compatibility").GetString());
        Assert.Empty(result.GetProperty("issues").EnumerateArray());

        using var catalogList = await catalogManager.GetAsync($"/api/forms/catalogs?search={usageSearch}&sort=usageCount&page=1&pageSize=20", Ct);
        var listed = await CaptureK6FixtureAsync(catalogList, "active-catalog-list-usage", HttpStatusCode.OK);
        var ordered = listed.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, ordered.Length);
        Assert.Equal(unusedCatalogId, ordered[0].GetProperty("id").GetGuid());
        Assert.Equal(0, ordered[0].GetProperty("usageCount").GetInt32());
        Assert.Equal(catalogId, ordered[1].GetProperty("id").GetGuid());
        Assert.Equal(1, ordered[1].GetProperty("usageCount").GetInt32());

        using var catalogDetail = await catalogManager.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(1, (await catalogDetail.ReadJsonAsync()).GetProperty("usageCount").GetInt32());
    }

    [Fact]
    public async Task Publication_check_requires_existing_choice_identity_and_reports_missing_optional_spanish_text()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]); var payload = Payload("Choice");
        payload["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "en", ["submitLabel"] = new JsonObject { ["en"] = "Finish" },
            ["fields"] = new JsonArray(new JsonObject { ["localId"] = "choice", ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["en"] = "Select" }, ["options"] = new JsonArray(
                    new JsonObject { ["localId"] = "one", ["label"] = new JsonObject { ["en"] = "One" } }) }) };
        var saved = await CreateAsync(client, payload); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var request = CheckPayload(saved);
        using var valid = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        var checkedResult = await valid.ReadJsonAsync();
        Assert.Equal("LocallyValidated", checkedResult.GetProperty("compatibility").GetString());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());
        Assert.Equal(["en"], checkedResult.GetProperty("channels")[0].GetProperty("cultures").EnumerateArray().Select(culture => culture.GetString()));
        var spanish = Assert.Single(checkedResult.GetProperty("cultures").EnumerateArray(), culture => culture.GetProperty("culture").GetString() == "es");
        Assert.False(spanish.GetProperty("isComplete").GetBoolean());
        Assert.Contains(spanish.GetProperty("missing").EnumerateArray(), issue => issue.GetProperty("path").GetString() == "draft.fields[0].options[0].label.es");
        var option = request["draft"]!["fields"]![0]!["options"]![0]!.AsObject();
        option["choiceId"] = Guid.NewGuid().ToString();
        using var foreign = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        option.Remove("choiceId");
        using var fresh = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode);
    }
    private static JsonObject CheckPayload(JsonElement saved)
    {
        var payload = EditPayload(saved); payload.Remove("requestId"); payload.Remove("offerWhen"); return payload;
    }

    private static async Task<JsonElement> CaptureK6FixtureAsync(HttpResponseMessage response, string name, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        using var document = JsonDocument.Parse(body);
        var result = document.RootElement.Clone();
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.Combine(output, "k6-source");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), body, Ct);
        }
        return result;
    }
}
