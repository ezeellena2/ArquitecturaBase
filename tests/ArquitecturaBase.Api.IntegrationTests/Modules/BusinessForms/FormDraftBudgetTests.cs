using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    private static readonly JsonSerializerOptions UnicodeRequestOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    [Fact]
    public async Task A_unicode_draft_within_the_request_budget_keeps_its_protected_receipt_and_replays_exact_text()
    {
        using var client = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]); var payload = Payload("Unicode result budget proof");
        var fields = new JsonArray();
        for (var i = 0; i < 40; i++)
        {
            var choices = new JsonArray(); for (var j = 0; j < 50; j++) choices.Add(new JsonObject
                { ["localId"] = "o" + j, ["label"] = new JsonObject { ["es"] = new string('漢', 70) + j } });
            fields.Add(new JsonObject { ["localId"] = "f" + i, ["type"] = "choice", ["label"] = new JsonObject { ["es"] = "Question " + i }, ["options"] = choices });
        }
        payload["draft"]!["fields"] = fields;
        var json = JsonSerializer.Serialize(payload, UnicodeRequestOptions);
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, 524288);
        using var response = await client.PostAsync("/api/forms/definitions", new StringContent(json, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.ReadJsonAsync();
        var savedFields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        Assert.Equal(40, savedFields.GetArrayLength());
        Assert.All(savedFields.EnumerateArray(), field => Assert.Equal(50, field.GetProperty("options").GetArrayLength()));
        Assert.Equal(new string('漢', 70) + "0", savedFields[0].GetProperty("options")[0].GetProperty("label").GetProperty("es").GetString());
        var requestId = Guid.Parse(payload["requestId"]!.GetValue<string>());
        var protectedBytes = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>()
            .Where(change => change.RequestId == requestId).Select(change => change.ProtectedResult!.Length).SingleAsync(Ct));
        Assert.InRange(protectedBytes, 1, 1048576);
        using var recovered = await client.GetAsync("/api/forms/draft-changes/" + payload["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.GetRawText()), JsonNode.Parse((await recovered.ReadJsonAsync()).GetProperty("result").GetRawText())));
        using var replay = await client.PostAsync("/api/forms/definitions", new StringContent(json, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.GetRawText()), JsonNode.Parse((await replay.ReadJsonAsync()).GetRawText())));
        using var list = await client.GetAsync("/api/forms/definitions?search=Unicode%20result%20budget%20proof", Ct);
        Assert.Equal(1, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task Openapi_declares_draft_check_acceptance_and_status_operations_with_problem_schemas()
    {
        var document = await OpenApiTests.ReadDocumentAsync(factory); var paths = document.GetProperty("paths");
        Assert.Equal(27, paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/forms/", StringComparison.Ordinal))
            .Sum(path => path.Value.EnumerateObject().Count(operation => operation.Name is "get" or "post" or "put")));
        foreach (var (suffix, contentType) in new[] { ("thumbnail", "image/jpeg"), ("content", "application/octet-stream") })
        {
            var file = paths.GetProperty("/api/forms/records/{recordId}/files/{fileId}/" + suffix).GetProperty("get");
            Assert.True(file.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty(contentType, out _));
        }
        var check = paths.GetProperty("/api/forms/definitions/{id}/publication-checks").GetProperty("post");
        Assert.True(check.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        Assert.True(check.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        var accept = paths.GetProperty("/api/forms/definitions/{id}/publications").GetProperty("post");
        Assert.True(accept.GetProperty("responses").GetProperty("202").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        Assert.True(paths.GetProperty("/api/forms/definitions/{id}/publications/{requestId}").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        var preview = paths.GetProperty("/api/forms/definitions/{id}/versions/{versionId}/preview").GetProperty("get");
        Assert.Contains(preview.GetProperty("parameters").EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "culture" && parameter.GetProperty("in").GetString() == "query");
        Assert.Equal("#/components/schemas/ProblemDetails", preview.GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
        var create = paths.GetProperty("/api/forms/definitions").GetProperty("post"); Assert.Equal("Forms", create.GetProperty("tags")[0].GetString());
        Assert.True(create.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(create.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        foreach (var operation in new[] { create, paths.GetProperty("/api/forms/definitions/{id}/draft").GetProperty("put"), paths.GetProperty("/api/forms/draft-changes/{requestId}").GetProperty("get") })
            Assert.Equal("#/components/schemas/ProblemDetails", operation.GetProperty("responses").GetProperty("410").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
    }
}
