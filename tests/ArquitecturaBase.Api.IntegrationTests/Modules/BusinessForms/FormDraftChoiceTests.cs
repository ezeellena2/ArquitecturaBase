using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Choice_rename_preserves_ids_and_retirement_and_parent_ownership_are_enforced()
    {
        using var client = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]); var payload = Payload();
        var fields = new JsonArray();
        for (var i = 0; i < 2; i++) fields.Add(new JsonObject { ["localId"] = "f" + i, ["type"] = "choice", ["label"] = new JsonObject { ["es"] = "Lista " + i },
            ["options"] = new JsonArray(new JsonObject { ["localId"] = "first", ["label"] = new JsonObject { ["es"] = "Uno" } },
                new JsonObject { ["localId"] = "second", ["label"] = new JsonObject { ["es"] = "Dos" } }) });
        payload["draft"]!["fields"] = fields; var original = await CreateAsync(client, payload); var id = original.GetProperty("definition").GetProperty("id").GetGuid();
        var foreign = EditPayload(original); foreign["draft"]!["fields"]![0]!["options"]![0]!["choiceId"] = foreign["draft"]!["fields"]![1]!["options"]![0]!["choiceId"]!.DeepClone();
        using var crossed = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", foreign, Ct); Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        Assert.True((await crossed.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0].options[0].choiceId", out _));
        var edit = EditPayload(original); edit["draft"]!["fields"]![0]!["options"]![0]!["label"]!["es"] = "Uno renombrado";
        edit["draft"]!["fields"]![0]!["options"]!.AsArray().RemoveAt(1);
        using var renamed = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.OK, renamed.StatusCode); var current = await renamed.ReadJsonAsync();
        Assert.Equal(original.GetProperty("identityMap").GetProperty("choices")[0].GetProperty("choiceId").GetGuid(), current.GetProperty("identityMap").GetProperty("choices")[0].GetProperty("choiceId").GetGuid());
        var resurrect = EditPayload(original); resurrect["expectedRevision"] = 2;
        using var retired = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", resurrect, Ct); Assert.Equal(HttpStatusCode.Conflict, retired.StatusCode);
        Assert.Equal("Forms.Draft.IdentityRetired", (await retired.ReadJsonAsync()).GetProperty("code").GetString());
    }
    [Fact]
    public async Task Equivalent_choice_labels_and_client_keys_are_rejected_without_a_partial_creation()
    {
        using var client = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]); var payload = Payload(); var field = payload["draft"]!["fields"]![0]!.AsObject();
        field["type"] = "choice"; field.Remove("help"); field["options"] = new JsonArray(
            new JsonObject { ["localId"] = "a", ["label"] = new JsonObject { ["es"] = "Café" } },
            new JsonObject { ["localId"] = "b", ["label"] = new JsonObject { ["es"] = " CAFE\u0301 " } });
        using var duplicate = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct); Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.True((await duplicate.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0].options[1].label.es", out _));
        field["options"]![1]!["label"]!["es"] = "Other"; field["key"] = "client_column";
        using var unknown = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct); Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Request.Invalid", (await unknown.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
