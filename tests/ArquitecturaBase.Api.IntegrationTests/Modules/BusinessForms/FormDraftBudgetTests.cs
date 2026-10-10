using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    private static readonly JsonSerializerOptions UnicodeRequestOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    [Fact]
    public async Task A_large_protected_unicode_result_returns_a_draft_error_and_rolls_back_everything()
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); var problem = await response.ReadJsonAsync();
        Assert.Equal("Forms.Draft.ResultTooLarge", problem.GetProperty("code").GetString()); Assert.True(problem.GetProperty("errors").TryGetProperty("draft", out _));
        using var missing = await client.GetAsync("/api/forms/draft-changes/" + payload["requestId"]!.GetValue<string>(), Ct); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var list = await client.GetAsync("/api/forms/definitions?search=Unicode%20result%20budget%20proof", Ct); Assert.Equal(0, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task Openapi_declares_six_draft_operations_with_problem_schemas()
    {
        var document = await OpenApiTests.ReadDocumentAsync(factory); var paths = document.GetProperty("paths");
        Assert.Equal(6, paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/forms/", StringComparison.Ordinal))
            .Sum(path => path.Value.EnumerateObject().Count(operation => operation.Name is "get" or "post" or "put")));
        var create = paths.GetProperty("/api/forms/definitions").GetProperty("post"); Assert.Equal("Forms", create.GetProperty("tags")[0].GetString());
        Assert.True(create.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(create.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        foreach (var operation in new[] { create, paths.GetProperty("/api/forms/definitions/{id}/draft").GetProperty("put"), paths.GetProperty("/api/forms/draft-changes/{requestId}").GetProperty("get") })
            Assert.Equal("#/components/schemas/ProblemDetails", operation.GetProperty("responses").GetProperty("410").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
    }
}
