using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// BI1-A with PostgreSQL: the library lists the installed standards, «Agregar» creates an own draft that remembers its standard and
// its bound questions (once per request), and a save that removes a bound question or changes its type is refused at it, while
// its text changes.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_standard_is_added_as_an_own_draft_whose_bound_questions_keep_what_the_standard_needs()
    {
        using var reader = await OperatorAsync(["formtemplates.read"]);
        using var designer = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]);

        using var library = await reader.GetAsync("/api/forms/library", Ct);
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);
        var standard = (await library.ReadJsonAsync()).GetProperty("standards").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == TestFormStandardSource.Key);
        Assert.Equal("Remito de prueba", standard.GetProperty("name").GetString());
        var copiesBefore = standard.GetProperty("copies").GetInt32();

        using var forbidden = await reader.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(Guid.NewGuid(), "RTA"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var missing = await designer.PostAsJsonAsync("/api/forms/library/test.missing/copies", Add(Guid.NewGuid(), "RTB"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var requestId = Guid.NewGuid();
        var prefix = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(requestId, prefix), Ct);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var definition = (await added.ReadJsonAsync()).GetProperty("definition");
        var fields = definition.GetProperty("draft").GetProperty("fields");
        var bound = definition.GetProperty("standard").GetProperty("bound");
        Assert.Equal(TestFormStandardSource.Key, definition.GetProperty("standard").GetProperty("key").GetString());
        Assert.Equal(6, bound.EnumerateObject().Count());
        Assert.Equal(fields[5].GetProperty("fieldId").GetGuid(), bound.GetProperty("total").GetGuid());
        Assert.Equal([fields[2].GetProperty("fieldId").GetGuid(), fields[3].GetProperty("fieldId").GetGuid()],
            fields[4].GetProperty("calculation").GetProperty("operands").EnumerateArray().Select(value => value.GetGuid()));

        using var repeated = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(requestId, prefix), Ct);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.Equal(definition.GetProperty("id").GetGuid(), (await repeated.ReadJsonAsync()).GetProperty("definition").GetProperty("id").GetGuid());
        using var after = await reader.GetAsync("/api/forms/library", Ct);
        Assert.Equal(copiesBefore + 1, (await after.ReadJsonAsync()).GetProperty("standards").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == TestFormStandardSource.Key).GetProperty("copies").GetInt32());

        var id = definition.GetProperty("id").GetGuid();
        var revision = definition.GetProperty("revision").GetInt64();
        async Task<HttpResponseMessage> SaveAsync(Func<JsonArray, JsonArray> change) => await designer.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", new JsonObject
        {
            ["requestId"] = Guid.NewGuid(), ["name"] = new JsonObject { ["es"] = "Remito propio" }, ["prefix"] = prefix, ["expectedRevision"] = revision,
            ["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "es", ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Send" }, ["fields"] = change(Inputs(fields)) },
        }, Ct);

        using var removed = await SaveAsync(inputs => { inputs.RemoveAt(5); return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, removed.StatusCode);
        Assert.Equal("Forms.Draft.BoundQuestionChanged", (await removed.ReadJsonAsync()).GetProperty("code").GetString());
        using var required = await SaveAsync(inputs => { inputs[0]!["required"] = false; return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, required.StatusCode);
        Assert.True((await required.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0]", out _));
        using var renamed = await SaveAsync(inputs => { inputs[0]!["label"] = new JsonObject { ["es"] = "Razón social", ["en"] = "Customer" }; inputs.RemoveAt(6); return inputs; });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    private static object Add(Guid requestId, string prefix) => new { requestId, name = new { es = "Remito propio " + prefix }, prefix };

    // The saved questions as the draft's input: their identity as both local id and field id, and what the editor sends back.
    private static JsonArray Inputs(JsonElement fields) => new(fields.EnumerateArray().Select(field =>
    {
        var input = new JsonObject
        {
            ["localId"] = field.GetProperty("fieldId").GetGuid().ToString(), ["fieldId"] = field.GetProperty("fieldId").GetGuid(),
            ["type"] = field.GetProperty("type").GetString(), ["label"] = JsonNode.Parse(field.GetProperty("label").GetRawText()),
            ["required"] = field.GetProperty("required").GetBoolean(),
        };
        foreach (var name in new[] { "maxLength", "rows", "repeated", "calculation" })
            if (field.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) input[name] = JsonNode.Parse(value.GetRawText());
        return (JsonNode?)input;
    }).ToArray());
}
