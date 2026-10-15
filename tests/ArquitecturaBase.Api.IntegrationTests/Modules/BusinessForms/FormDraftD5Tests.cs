using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Localized_name_and_incomplete_translations_save_and_recover_without_fallback()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var request = Payload();
        request["name"] = new JsonObject { ["es"] = "Formulario", ["en"] = "" };
        request["draft"]!["submitLabel"]!["en"] = "";
        request["draft"]!["fields"]![0]!["label"]!["en"] = "";
        request["draft"]!["fields"]![0]!["help"] = new JsonObject { ["es"] = "Ayuda" };
        var saved = await CreateAsync(client, request);
        var definition = saved.GetProperty("definition");
        Assert.Equal("", definition.GetProperty("name").GetProperty("en").GetString());
        using var recovered = await client.GetAsync("/api/forms/draft-changes/" + saved.GetProperty("requestId").GetGuid(), Ct);
        Assert.Equal(saved.GetRawText(), (await recovered.ReadJsonAsync()).GetProperty("result").GetRawText());
        using var check = await client.PostAsJsonAsync("/api/forms/definitions/" + definition.GetProperty("id").GetGuid() + "/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var result = await check.ReadJsonAsync();
        Assert.Equal("Blocked", result.GetProperty("compatibility").GetString());
        var issues = result.GetProperty("issues").EnumerateArray().ToArray();
        foreach (var expected in new[] { "name.en", "draft.submitLabel.en", "draft.fields[0].label.en", "draft.fields[0].help.en" })
            Assert.Contains(issues, issue => issue.GetProperty("path").GetString() == expected && issue.GetProperty("culture").GetString() == "en");
    }
    [Fact]
    public async Task Explicit_translation_retirement_and_refill_preserve_ids_revision_and_exact_replay()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(client, Payload("Traducciones"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var omitted = EditPayload(saved); omitted["name"]!.AsObject().Remove("en");
        using var lost = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", omitted, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, lost.StatusCode);
        Assert.Equal("Forms.Draft.TranslationsWouldBeLost", (await lost.ReadJsonAsync()).GetProperty("code").GetString());
        var pending = EditPayload(saved); pending["name"]!["en"] = " ";
        pending["draft"]!["submitLabel"]!["en"] = "";
        pending["draft"]!["fields"]![0]!["label"]!["en"] = "";
        pending["draft"]!["fields"]![0]!["help"]!["en"] = null;
        using var retired = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", pending, Ct);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var incomplete = await retired.ReadJsonAsync();
        Assert.Equal("", incomplete.GetProperty("definition").GetProperty("name").GetProperty("en").GetString());
        Assert.Equal(2, incomplete.GetProperty("definition").GetProperty("revision").GetInt64());
        using var replay = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", pending, Ct);
        Assert.Equal(incomplete.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        var withoutEmpty = EditPayload(incomplete); withoutEmpty["name"]!.AsObject().Remove("en");
        withoutEmpty["draft"]!["submitLabel"]!.AsObject().Remove("en");
        withoutEmpty["draft"]!["fields"]![0]!["label"]!.AsObject().Remove("en");
        using var omitEmpty = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", withoutEmpty, Ct);
        Assert.Equal(HttpStatusCode.OK, omitEmpty.StatusCode);
        var omittedEmpty = await omitEmpty.ReadJsonAsync();
        var complete = EditPayload(omittedEmpty); complete["name"]!["en"] = "Translations";
        complete["draft"]!["submitLabel"]!["en"] = "Submit";
        complete["draft"]!["fields"]![0]!["label"]!["en"] = "Name";
        complete["draft"]!["fields"]![0]!["help"]!["en"] = "Your name";
        using var refilled = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", complete, Ct);
        Assert.Equal(HttpStatusCode.OK, refilled.StatusCode);
        var final = await refilled.ReadJsonAsync();
        Assert.Equal(4, final.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(), final.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid());
        Assert.Equal(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString(), final.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString());
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(final), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.Equal("LocallyValidated", checkedResult.GetProperty("compatibility").GetString());
        Assert.Equal("flow-static-7.3-r6", checkedResult.GetProperty("channels")[0].GetProperty("profileRevision").GetString());
        var changedName = CheckPayload(final); changedName["name"]!["en"] = "Unsaved name";
        using var unsaved = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", changedName, Ct);
        Assert.Equal(HttpStatusCode.Conflict, unsaved.StatusCode);
    }
    [Fact]
    public async Task Localized_names_remain_closed_validated_redacted_and_available_in_list_and_detail()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Nombre buscable"); payload["name"]!["en"] = "Searchable name";
        var saved = await CreateAsync(client, payload); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        using var list = await client.GetAsync("/api/forms/definitions?search=Nombre%20buscable&sort=name", Ct);
        var listed = await list.ReadJsonAsync();
        Assert.Equal("Searchable name", listed.GetProperty("items")[0].GetProperty("name").GetProperty("en").GetString());
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(saved.GetProperty("definition").GetRawText(), (await detail.ReadJsonAsync()).GetProperty("definition").GetRawText());
        foreach (var invalidName in new JsonNode?[] {
            JsonValue.Create("Name"),
            new JsonObject { ["en"] = "No original" },
            new JsonObject { ["es"] = "Name", ["fr"] = "Nom" },
            new JsonObject { ["es"] = "Name", ["en"] = null },
            new JsonObject { ["es"] = "Name", ["en"] = new string('x', 121) } })
        {
            var invalid = Payload(); invalid["name"] = invalidName;
            using var response = await client.PostAsJsonAsync("/api/forms/definitions", invalid, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var englishList = await client.GetAsync("/api/forms/definitions?search=Searchable%20name&sort=name", Ct);
        Assert.Equal(1, (await englishList.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var englishFacets = await client.GetAsync("/api/forms/definitions/facets?search=Searchable%20name", Ct);
        Assert.Equal(1, (await englishFacets.ReadJsonAsync()).GetProperty("all").GetInt32());
        var request = new ArquitecturaBase.Api.Modules.BusinessForms.Contracts.SaveFormDraftHttpRequest(Guid.NewGuid(),
            new Dictionary<string, string> { ["es"] = "Private business name", ["en"] = "Private translated name" }, "FORM", null!);
        Assert.DoesNotContain("Private", request.ToString(), StringComparison.Ordinal);
    }    [Fact]
    public async Task Visible_name_search_order_and_facets_follow_ui_language_and_preserve_pending_translation()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var token = "Locale" + Guid.NewGuid().ToString("N")[..8];
        var a = Payload(token + "A"); a["name"]!["en"] = token + "Z";
        var b = Payload(token + "Z"); b["name"]!["en"] = token + "A";
        var first = await CreateAsync(client, a); var second = await CreateAsync(client, b);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var spanish = await client.GetAsync("/api/forms/definitions?search=" + token + "&sort=name&pageSize=1", Ct);
        Assert.Equal(first.GetProperty("definition").GetProperty("id").GetGuid(), (await spanish.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        client.DefaultRequestHeaders.AcceptLanguage.Clear(); client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var english = await client.GetAsync("/api/forms/definitions?search=" + token + "&sort=name&pageSize=1", Ct);
        Assert.Equal(second.GetProperty("definition").GetProperty("id").GetGuid(), (await english.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        using var facets = await client.GetAsync("/api/forms/definitions/facets?search=" + token, Ct);
        Assert.Equal(2, (await facets.ReadJsonAsync()).GetProperty("all").GetInt32());
        var pending = Payload(token + "M"); pending["name"]!["en"] = "";
        var third = await CreateAsync(client, pending);
        using var reference = await client.GetAsync("/api/forms/definitions?search=" + token + "M&sort=name", Ct);
        var row = (await reference.ReadJsonAsync()).GetProperty("items")[0];
        Assert.Equal(third.GetProperty("definition").GetProperty("id").GetGuid(), row.GetProperty("id").GetGuid());
        Assert.Equal("", row.GetProperty("name").GetProperty("en").GetString());
    }
}
