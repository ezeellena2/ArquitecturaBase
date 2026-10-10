using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("submitLabel", "draft.submitLabel")]
    [InlineData("help", "draft.fields[0].help")]
    public async Task Http_omitting_existing_original_text_does_not_silently_reset_or_erase_it(string property, string path)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Original text preservation");
        payload["draft"]!["submitLabel"] = new JsonObject { ["es"] = "Confirmar mi visita" };
        payload["draft"]!["fields"]![0]!["help"] = new JsonObject { ["es"] = "Conservá esta ayuda original" };
        var saved = await CreateAsync(actor, payload);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var edit = EditPayload(saved);
        if (property == "submitLabel") edit["draft"]!.AsObject().Remove(property);
        else edit["draft"]!["fields"]![0]!.AsObject().Remove(property);
        using var rejected = await actor.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.ReadJsonAsync();
        Assert.Equal("Forms.Draft.TranslationsWouldBeLost", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(path, out _));
        using var detail = await actor.GetAsync($"/api/forms/definitions/{id}", Ct);
        var current = (await detail.ReadJsonAsync()).GetProperty("definition");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.GetProperty("definition").GetProperty("draft").GetRawText()), JsonNode.Parse(current.GetProperty("draft").GetRawText())));
        Assert.Equal(saved.GetProperty("definition").GetProperty("revision").GetInt64(), current.GetProperty("revision").GetInt64());
        using var missingReceipt = await actor.GetAsync("/api/forms/draft-changes/" + edit["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingReceipt.StatusCode);
    }

    [Fact]
    public async Task Http_explicit_help_retirement_and_creation_defaults_remain_available()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Explicit help retirement");
        payload["draft"]!.AsObject().Remove("submitLabel");
        payload["draft"]!["fields"]![0]!["help"] = new JsonObject { ["es"] = "Ayuda para retirar" };
        var saved = await CreateAsync(actor, payload);
        Assert.Equal("Enviar", saved.GetProperty("definition").GetProperty("draft").GetProperty("submitLabel").GetProperty("es").GetString());
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var edit = EditPayload(saved);
        edit["draft"]!["fields"]![0]!["help"]!["es"] = null;
        edit["draft"]!["submitLabel"]!["es"] = "Confirmar visita";
        using var response = await actor.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.ReadJsonAsync();
        Assert.Equal("Confirmar visita", current.GetProperty("definition").GetProperty("draft").GetProperty("submitLabel").GetProperty("es").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, current.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("help").ValueKind);
        Assert.Equal(2, current.GetProperty("definition").GetProperty("revision").GetInt64());
    }
}
