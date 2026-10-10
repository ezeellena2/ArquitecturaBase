using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    private async Task<HttpClient> AdminAsync()
    {
        using var signedIn = factory.CreateClient(); await signedIn.LoginAsync(factory, ApiFactory.AdminEmail);
        var actor = await factory.ExecuteDbContextAsync(db => db.Users.Where(x => x.Email == ApiFactory.AdminEmail).Select(x => x.Id).SingleAsync(Ct));
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString()); return client;
    }
    private static JsonObject Payload(string name = "Draft") => new()
    {
        ["requestId"] = Guid.NewGuid().ToString(), ["name"] = new JsonObject { ["es"] = name, ["en"] = name }, ["prefix"] = "T" + Guid.NewGuid().ToString("N")[..10],
        ["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "es", ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(new JsonObject { ["localId"] = "first", ["type"] = "shortText", ["label"] = new JsonObject { ["es"] = "Nombre", ["en"] = "Name" },
                ["help"] = new JsonObject { ["es"] = "Tu nombre", ["en"] = "Your name" } }) },
    };
    private static async Task<JsonElement> CreateAsync(HttpClient client, JsonObject payload)
    {
        using var response = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.ReadJsonAsync();
    }
    private static JsonObject EditPayload(JsonElement saved)
    {
        var definition = saved.GetProperty("definition"); var payload = JsonNode.Parse(definition.GetRawText())!.AsObject();
        payload.Remove("id"); payload.Remove("prefixLocked"); payload.Remove("revision");
        payload["requestId"] = Guid.NewGuid().ToString(); payload["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = payload["draft"]!["fields"]!.AsArray();
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i]!["localId"] = "f" + i; fields[i]!.AsObject().Remove("key"); fields[i]!.AsObject().Remove("replacesFieldId");
            if (fields[i]!["options"] is JsonArray options)
                for (var j = 0; j < options.Count; j++) options[j]!["localId"] = "o" + j;
        }
        return payload;
    }
    [Fact]
    public async Task Recovery_preserves_the_original_snapshot_after_later_saves_and_rejects_uuid_reuse()
    {
        using var client = await AdminAsync(); var original = await CreateAsync(client, Payload("Snapshot before later save"));
        var id = original.GetProperty("definition").GetProperty("id").GetGuid(); var edit = EditPayload(original);
        edit["name"] = new JsonObject { ["es"] = "Snapshot after later save", ["en"] = "Snapshot after later save" };
        using var saved = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var current = await saved.ReadJsonAsync(); Assert.Equal(2, current.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(original.GetProperty("identityMap").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(), current.GetProperty("identityMap").GetProperty("fields")[0].GetProperty("fieldId").GetGuid());
        using var recovery = await client.GetAsync("/api/forms/draft-changes/" + original.GetProperty("requestId").GetGuid(), Ct);
        var recovered = await recovery.ReadJsonAsync(); Assert.Equal(2, recovered.GetProperty("currentRevision").GetInt64());
        Assert.Equal(original.GetRawText(), recovered.GetProperty("result").GetRawText());
        edit["name"] = new JsonObject { ["es"] = "Different content same UUID", ["en"] = "Different content same UUID" };
        using var reuse = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
        Assert.Equal("Forms.Draft.RequestConflict", (await reuse.ReadJsonAsync()).GetProperty("code").GetString());
        edit["requestId"] = Guid.NewGuid().ToString();
        using var stale = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.Draft.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        using var listed = await client.GetAsync("/api/forms/definitions?search=Snapshot%20after&sort=-questionCount", Ct);
        Assert.Equal(1, (await listed.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var facets = await client.GetAsync("/api/forms/definitions/facets?search=Snapshot%20after&publicationFilter=Published", Ct);
        var counts = await facets.ReadJsonAsync(); Assert.Equal(1, counts.GetProperty("drafts").GetInt32()); Assert.Equal(0, counts.GetProperty("published").GetInt32());
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct); var loaded = await detail.ReadJsonAsync();
        Assert.True(loaded.GetProperty("capabilities").GetProperty("canSave").GetBoolean());
        Assert.Equal(loaded.GetProperty("audit").GetProperty("modifiedAtUtc").GetString(), loaded.GetProperty("audit").GetProperty("lastChangedAtUtc").GetString());
    }
    [Fact]
    public async Task Retired_ids_cannot_be_resurrected_and_type_changes_receive_a_new_identity()
    {
        using var client = await AdminAsync(); var original = await CreateAsync(client, Payload());
        var id = original.GetProperty("definition").GetProperty("id").GetGuid(); var replaced = EditPayload(original);
        var field = replaced["draft"]!["fields"]![0]!.AsObject(); field["replacesFieldId"] = field["fieldId"]!.DeepClone(); field.Remove("fieldId");
        field["type"] = "date"; field.Remove("maxLength"); field.Remove("options");
        using var changed = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", replaced, Ct); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var current = await changed.ReadJsonAsync();
        Assert.NotEqual(original.GetProperty("identityMap").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(), current.GetProperty("identityMap").GetProperty("fields")[0].GetProperty("fieldId").GetGuid());
        var resurrect = EditPayload(original); resurrect["expectedRevision"] = 2;
        using var failure = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", resurrect, Ct); Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal("Forms.Draft.IdentityRetired", (await failure.ReadJsonAsync()).GetProperty("code").GetString());
        var remove = EditPayload(current); remove["draft"]!["fields"] = new JsonArray();
        using var removed = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", remove, Ct); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var retry = EditPayload(current); retry["expectedRevision"] = 3;
        using var failureAgain = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", retry, Ct); Assert.Equal(HttpStatusCode.Conflict, failureAgain.StatusCode);
    }
    [Fact]
    public async Task Translations_foreign_ids_duplicate_local_ids_and_invalid_body_return_field_errors()
    {
        using var client = await AdminAsync(); var original = await CreateAsync(client, Payload()); var id = original.GetProperty("definition").GetProperty("id").GetGuid();
        var edit = EditPayload(original); edit["draft"]!["fields"]![0]!["label"]!.AsObject().Remove("en");
        using var lost = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.BadRequest, lost.StatusCode);
        Assert.Equal("Forms.Draft.TranslationsWouldBeLost", (await lost.ReadJsonAsync()).GetProperty("code").GetString());
        edit = EditPayload(original); edit["draft"]!["fields"]![0]!["fieldId"] = Guid.NewGuid().ToString();
        using var foreign = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.True((await foreign.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0].fieldId", out _));
        var duplicate = Payload(); duplicate["draft"]!["fields"]!.AsArray().Add(duplicate["draft"]!["fields"]![0]!.DeepClone());
        using var invalid = await client.PostAsJsonAsync("/api/forms/definitions", duplicate, Ct); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[1].localId", out _));
        using var unreadable = await client.PostAsync("/api/forms/definitions", new StringContent("{", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal("Request.Invalid", (await unreadable.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
