using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("name", "es", "Visita\0")]
    [InlineData("name", "en", "Visit\0")]
    [InlineData("label", "es", "\u200B")]
    [InlineData("label", "en", "\u200D")]
    [InlineData("choice", "es", "\u0301")]
    [InlineData("choice", "en", "Choice\0")]
    [InlineData("help", "es", "\u200B")]
    [InlineData("submitLabel", "en", "\u200B")]
    public async Task Http_draft_text_rejects_invalid_creation_without_a_definition_or_receipt(string target, string culture, string value)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        actor.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var request = ChoiceTextPayload();
        var path = SetInvalidText(request, target, culture, value);
        using var rejected = await actor.PostAsJsonAsync("/api/forms/definitions", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.ReadJsonAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(path, out _));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        var prefix = request["prefix"]!.GetValue<string>().ToUpperInvariant();
        var requestId = Guid.Parse(request["requestId"]!.GetValue<string>());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().CountAsync(row => row.Prefix == prefix, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().CountAsync(row => row.RequestId == requestId, Ct)));
    }

    [Theory]
    [InlineData("name", "es", "Visita\0")]
    [InlineData("name", "en", "Visit\0")]
    [InlineData("label", "es", "\u200B")]
    [InlineData("label", "en", "\u200D")]
    [InlineData("choice", "es", "\u0301")]
    [InlineData("choice", "en", "Choice\0")]
    [InlineData("help", "es", "\u200B")]
    [InlineData("submitLabel", "en", "\u200B")]
    public async Task Http_draft_text_rejects_invalid_editing_without_losing_the_saved_snapshot(string target, string culture, string value)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        actor.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var saved = await CreateAsync(actor, ChoiceTextPayload());
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var request = EditPayload(saved);
        var path = SetInvalidText(request, target, culture, value);
        using var rejected = await actor.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.ReadJsonAsync()).GetProperty("errors").TryGetProperty(path, out _));
        using var loaded = await actor.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.GetProperty("definition").GetRawText()),
            JsonNode.Parse((await loaded.ReadJsonAsync()).GetProperty("definition").GetRawText())));
        var requestId = Guid.Parse(request["requestId"]!.GetValue<string>());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().CountAsync(row => row.RequestId == requestId, Ct)));
    }

    [Fact]
    public async Task Http_draft_text_keeps_visible_unicode_and_multiline_help_on_reload()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var request = ChoiceTextPayload();
        request["name"]!["es"] = "Visita 👩‍💻";
        request["name"]!["en"] = "Visit 👩‍💻";
        var field = request["draft"]!["fields"]![0]!.AsObject();
        field["label"]!["es"] = "👩‍💻";
        field["label"]!["en"] = "汉字";
        field["help"]!["es"] = "Primera línea\n\tDetalle 👩‍💻";
        field["help"]!["en"] = "First line\n\tDetails 👩‍💻";
        var saved = await CreateAsync(actor, request);
        var definition = saved.GetProperty("definition");
        using var loaded = await actor.GetAsync($"/api/forms/definitions/{definition.GetProperty("id").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
        var reloaded = (await loaded.ReadJsonAsync()).GetProperty("definition");
        Assert.Equal("Visita 👩‍💻", reloaded.GetProperty("name").GetProperty("es").GetString());
        var reloadedField = reloaded.GetProperty("draft").GetProperty("fields")[0];
        Assert.Equal("👩‍💻", reloadedField.GetProperty("label").GetProperty("es").GetString());
        Assert.Equal("汉字", reloadedField.GetProperty("label").GetProperty("en").GetString());
        Assert.Equal("Primera línea\n\tDetalle 👩‍💻", reloadedField.GetProperty("help").GetProperty("es").GetString());
        Assert.Equal("First line\n\tDetails 👩‍💻", reloadedField.GetProperty("help").GetProperty("en").GetString());
    }

    private static JsonObject ChoiceTextPayload()
    {
        var request = Payload("Texto visible");
        var field = request["draft"]!["fields"]![0]!.AsObject();
        field["type"] = "choice";
        field["options"] = new JsonArray(new JsonObject
        {
            ["localId"] = "option", ["label"] = new JsonObject { ["es"] = "Opción", ["en"] = "Option" },
        });
        return request;
    }

    private static string SetInvalidText(JsonObject request, string target, string culture, string value)
    {
        var draft = request["draft"]!.AsObject();
        var field = draft["fields"]![0]!.AsObject();
        var text = target switch
        {
            "name" => request["name"]!.AsObject(),
            "submitLabel" => draft["submitLabel"]!.AsObject(),
            "choice" => field["options"]![0]!["label"]!.AsObject(),
            _ => field[target]!.AsObject(),
        };
        text[culture] = value;
        return target switch
        {
            "name" => "name." + culture,
            "submitLabel" => "draft.submitLabel." + culture,
            "choice" => "draft.fields[0].options[0].label." + culture,
            _ => "draft.fields[0]." + target + "." + culture,
        };
    }
}
