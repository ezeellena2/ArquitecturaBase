using System.Net;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantExecutionProviderHandler(AiProvider provider, string toolId,
    AiAssistantTransactionProbe transactions) : HttpMessageHandler
{
    public List<JsonElement> Requests { get; } = [];
    public Guid RoleId { get; set; }
    public List<int> ActiveTransactionsDuringHttp { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(provider == AiProvider.OpenAI ? "https://api.openai.com/v1/responses" : "https://api.anthropic.com/v1/messages", request.RequestUri?.AbsoluteUri);
        Assert.Equal(0, transactions.ActiveTransactions);
        Assert.Equal("fictional-assistant-key", provider == AiProvider.OpenAI
            ? request.Headers.Authorization?.Parameter : Assert.Single(request.Headers.GetValues("x-api-key")));
        ActiveTransactionsDuringHttp.Add(transactions.ActiveTransactions);
        var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        using (json) Requests.Add(json.RootElement.Clone());
        Assert.Equal("fictional-model", Requests[^1].GetProperty("model").GetString());
        if (provider == AiProvider.OpenAI) Assert.False(Requests[^1].GetProperty("store").GetBoolean());
        Assert.Contains(Requests[^1].GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == toolId.Replace('.', '_'));
        var response = ToolResponse(Requests[^1]);
        return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }

    private string ToolResponse(JsonElement request)
    {
        var terminal = Requests.Count % 2 == 0;
        object arguments;
        var name = toolId.Replace('.', '_');
        if (terminal)
        {
            var result = provider == AiProvider.OpenAI
                ? request.GetProperty("input").EnumerateArray().Last(item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output").GetProperty("output").GetString()!
                : request.GetProperty("messages").EnumerateArray().Last().GetProperty("content")[0].GetProperty("content").GetString()!;
            using var evidence = JsonDocument.Parse(result);
            Assert.Equal("complete", evidence.RootElement.GetProperty("state").GetString());
            var value = evidence.RootElement.GetProperty("evidence");
            Assert.Equal(toolId, value.GetProperty("sourceId").GetString());
            var text = string.Join(" | ", value.GetProperty("rows").EnumerateArray()
                .SelectMany(row => row.GetProperty("values").EnumerateArray())
                .Select(field => field.GetProperty("value").GetString()));
            arguments = new { kind = "answer", text, evidenceIds = new[] { value.GetProperty("id").GetGuid() } };
            name = "assistant_answer";
        }
        else arguments = toolId switch
        {
            "roles.permissions" when provider == AiProvider.OpenAI => new { roleId = (Guid?)RoleId, roleName = (string?)null },
            "roles.permissions" => new { roleId = (Guid?)null, roleName = "Support" },
            "system.help" => new { topic = "profile" },
            _ => new { },
        };
        var id = "execution-call-" + Requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return provider == AiProvider.OpenAI
            ? JsonSerializer.Serialize(new
            {
                status = "completed", output = new object[]
                {
                    new { type = "reasoning", id = "reasoning-" + id, summary = Array.Empty<object>(), encrypted_content = "opaque-test-reasoning" },
                    new { type = "function_call", id = "item-" + id, call_id = id, name, arguments = JsonSerializer.Serialize(arguments), status = "completed" },
                }, usage = new { input_tokens = 10, output_tokens = 2 },
            })
            : JsonSerializer.Serialize(new
            {
                type = "message", role = "assistant", stop_reason = "tool_use", content = new object[]
                {
                    new { type = "thinking", thinking = "Simulated provider reasoning", signature = "opaque-test-signature" },
                    new { type = "tool_use", id, name, input = arguments },
                }, usage = new { input_tokens = 10, output_tokens = 2 },
            });
    }
}
