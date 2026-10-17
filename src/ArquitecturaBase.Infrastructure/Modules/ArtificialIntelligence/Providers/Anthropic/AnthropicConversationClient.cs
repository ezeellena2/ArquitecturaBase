using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;

internal sealed class AnthropicConversationClient(HttpClient client, LlmConversationHttpTransport transport) : ILlmConversationClient
{
    public async Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != AiProvider.Anthropic) return AiAssistantErrors.Invalid;
        try
        {
            var names = LlmConversationHttpTransport.ToolNames(request);
            var messages = transport.ReadItems(request, AiProvider.Anthropic);
            AppendToolResults(messages, request.ToolResults, names);
            var tools = request.Tools.Select(tool => new
            {
                name = LlmConversationHttpTransport.ProviderToolName(tool.Name), description = tool.Description,
                input_schema = LlmConversationHttpTransport.ParseObject(tool.ParametersJson)
            }).ToArray();
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.anthropic.com/v1/messages"));
            message.Headers.Add("x-api-key", request.ApiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            message.Content = JsonContent.Create(new
            {
                model = request.Model, system = request.SystemPrompt, messages, tools, max_tokens = request.MaxOutputTokens,
                tool_choice = new { type = "auto", disable_parallel_tool_use = true }
            });
            return await transport.SendAsync(client, message, request, root => Parse(root, messages, names, request.MaxContinuationBytes), cancellationToken);
        }
        catch (JsonException) { return AiAssistantErrors.Invalid; }
    }

    private static void AppendToolResults(List<JsonElement> messages, IReadOnlyList<LlmToolResult> results, Dictionary<string, string> names)
    {
        if (messages.Count == 0) throw new JsonException("A conversation needs a user message.");
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        var last = messages[^1];
        if (LlmConversationHttpTransport.Text(last, "role") == "assistant" && last.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (LlmConversationHttpTransport.Text(block, "type") != "tool_use") continue;
                var name = LlmConversationHttpTransport.RequiredText(block, "name");
                if (!names.TryGetValue(name, out var original) || !pending.TryAdd(LlmConversationHttpTransport.RequiredText(block, "id"), original))
                    throw new JsonException("Invalid prior tool call.");
            }
        }
        if (pending.Count != results.Count) throw new JsonException("Tool results must immediately answer every pending call exactly once.");
        if (results.Count == 0) return;
        var blocks = new List<JsonElement>();
        foreach (var result in results)
        {
            if (!pending.Remove(result.CallId, out var name) || name != result.Name) throw new JsonException("Tool result does not match its call.");
            _ = LlmConversationHttpTransport.ParseObject(result.ResultJson);
            blocks.Add(JsonSerializer.SerializeToElement(new { type = "tool_result", tool_use_id = result.CallId, content = result.ResultJson }));
        }
        messages.Add(JsonSerializer.SerializeToElement(new { role = "user", content = blocks }));
    }

    private Result<LlmConversationResponse> Parse(JsonElement root, List<JsonElement> messages, Dictionary<string, string> names, int continuationBytes)
    {
        if (LlmConversationHttpTransport.Text(root, "type") != "message") return AiAssistantErrors.InvalidOutput;
        var stop = LlmConversationHttpTransport.Text(root, "stop_reason");
        if (stop is "max_tokens" or "pause_turn" or "model_context_window_exceeded") return LlmConversationHttpTransport.TerminalResponse(root, "incomplete");
        if (stop == "refusal") return LlmConversationHttpTransport.TerminalResponse(root, "refusal");
        if (stop != "tool_use" || !root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return AiAssistantErrors.InvalidOutput;
        var calls = new List<LlmToolCall>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prior in messages)
        {
            if (LlmConversationHttpTransport.Text(prior, "role") != "assistant" || !prior.TryGetProperty("content", out var priorContent)
                || priorContent.ValueKind != JsonValueKind.Array) continue;
            foreach (var block in priorContent.EnumerateArray())
                if (LlmConversationHttpTransport.Text(block, "type") == "tool_use") ids.Add(LlmConversationHttpTransport.RequiredText(block, "id"));
        }
        foreach (var block in content.EnumerateArray())
        {
            var type = LlmConversationHttpTransport.RequiredText(block, "type");
            if (type == "tool_use")
            {
                var callId = LlmConversationHttpTransport.RequiredText(block, "id");
                var name = LlmConversationHttpTransport.RequiredText(block, "name");
                if (!names.TryGetValue(name, out var original)) return AiAssistantErrors.ToolRejected;
                if (!ids.Add(callId) || callId.Length > 128 || calls.Count >= transport.MaximumToolCalls) return AiAssistantErrors.InvalidOutput;
                if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object) return AiAssistantErrors.InvalidOutput;
                calls.Add(new(callId, original, input.GetRawText()));
            }
            else if (type is not ("text" or "thinking" or "redacted_thinking")) return AiAssistantErrors.InvalidOutput;
        }
        if (calls.Count == 0) return AiAssistantErrors.InvalidOutput;
        messages.Add(JsonSerializer.SerializeToElement(new { role = "assistant", content }));
        var continuation = transport.Continuation(AiProvider.Anthropic, messages, continuationBytes);
        if (continuation.IsFailure) return continuation.Error;
        return new LlmConversationResponse("tools", calls, continuation.Value, LlmConversationHttpTransport.Usage(root));
    }
}
