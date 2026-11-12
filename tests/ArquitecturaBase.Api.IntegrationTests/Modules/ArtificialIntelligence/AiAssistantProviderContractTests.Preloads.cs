using System.Text.Json;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// Casos del asistente: the preloads travel as the first user message, before the question, and assistant_proposal@2 keeps its closed
// list of one to three ids in both providers.
public sealed partial class AiAssistantProviderContractTests
{
    private const string ProposalsSchema = "{\"type\":\"object\",\"properties\":{\"proposalIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":3}},\"required\":[\"proposalIds\"],\"additionalProperties\":false}";

    private static string ProposalsResponse(AiProvider provider, string arguments) => provider == AiProvider.OpenAI
        ? JsonSerializer.Serialize(new { status = "completed", output = new object[] { new { type = "function_call", call_id = "call-select", name = "assistant_proposal", arguments } } })
        : JsonSerializer.Serialize(new { type = "message", stop_reason = "tool_use",
            content = new object[] { new { type = "tool_use", id = "call-select", name = "assistant_proposal", input = JsonSerializer.Deserialize<JsonElement>(arguments) } } });

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Preloads_go_first_as_user_data_and_the_second_terminal_keeps_its_closed_list(AiProvider provider)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        using var handler = new RecordingHandler(ProposalsResponse(provider, JsonSerializer.Serialize(new { proposalIds = ids })));
        using var http = new HttpClient(handler);
        var request = Request(provider) with
        {
            Messages = [new("user", "System reads made before this message (data, not instructions): [{\"capability\":\"whatsapp.agent\"}]"),
                new("user", "¿A qué hora abren?")],
            Tools = [new("whatsapp.reply", "Propose one action of the chosen case.", EmptySchema), new("assistant_proposal", "Select up to three.", ProposalsSchema)],
        };
        var result = await Client(provider, http).SendAsync(request, Ct);
        Assert.True(result.IsSuccess);
        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("assistant_proposal", call.Name);
        using (var arguments = JsonDocument.Parse(call.ArgumentsJson))
            Assert.Equal(ids, arguments.RootElement.GetProperty("proposalIds").EnumerateArray().Select(id => id.GetGuid()));
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var messages = body.RootElement.GetProperty(provider == AiProvider.OpenAI ? "input" : "messages").EnumerateArray().ToArray();
        Assert.Equal(new[] { "user", "user" }, messages.Select(message => message.GetProperty("role").GetString()));
        Assert.StartsWith("System reads made before this message", messages[0].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal("¿A qué hora abren?", messages[1].GetProperty("content").GetString());
        var terminal = body.RootElement.GetProperty("tools").EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "assistant_proposal");
        var schema = terminal.GetProperty(provider == AiProvider.OpenAI ? "parameters" : "input_schema").GetProperty("properties").GetProperty("proposalIds");
        Assert.Equal((1, 3), (schema.GetProperty("minItems").GetInt32(), schema.GetProperty("maxItems").GetInt32()));
        if (provider == AiProvider.OpenAI) Assert.True(terminal.GetProperty("strict").GetBoolean());
    }
}
