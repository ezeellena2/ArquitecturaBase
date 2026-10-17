using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;

internal sealed class OpenAiConversationClient(HttpClient client, LlmConversationHttpTransport transport) : ILlmConversationClient
{
    private static readonly string[] ContinuationIncludes = ["reasoning.encrypted_content"];

    public async Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != AiProvider.OpenAI) return AiAssistantErrors.Invalid;
        try
        {
            var names = LlmConversationHttpTransport.ToolNames(request);
            var items = transport.ReadItems(request, AiProvider.OpenAI);
            AppendToolResults(items, request.ToolResults, names);
            var tools = request.Tools.Select(tool => new
            {
                type = "function", name = LlmConversationHttpTransport.ProviderToolName(tool.Name),
                description = tool.Description, parameters = LlmConversationHttpTransport.ParseObject(tool.ParametersJson), strict = true
            }).ToArray();
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.openai.com/v1/responses"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
            message.Content = JsonContent.Create(new
            {
                model = request.Model, instructions = request.SystemPrompt, input = items, tools,
                max_output_tokens = request.MaxOutputTokens, store = false, parallel_tool_calls = false,
                include = ContinuationIncludes
            });
            return await transport.SendAsync(client, message, request, root => Parse(root, items, names, request.MaxContinuationBytes), cancellationToken);
        }
        catch (JsonException) { return AiAssistantErrors.Invalid; }
    }

    private static void AppendToolResults(List<JsonElement> items, IReadOnlyList<LlmToolResult> results, Dictionary<string, string> names)
    {
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (LlmConversationHttpTransport.Text(item, "type") == "function_call")
            {
                var name = LlmConversationHttpTransport.RequiredText(item, "name");
                if (!names.TryGetValue(name, out var original) || !pending.TryAdd(LlmConversationHttpTransport.RequiredText(item, "call_id"), original))
                    throw new JsonException("Invalid prior tool call.");
            }
            else if (LlmConversationHttpTransport.Text(item, "type") == "function_call_output")
            {
                if (!pending.Remove(LlmConversationHttpTransport.RequiredText(item, "call_id"))) throw new JsonException("Invalid prior tool result.");
            }
        }
        if (pending.Count != results.Count) throw new JsonException("Tool results must answer every pending call exactly once.");
        foreach (var result in results)
        {
            if (!pending.Remove(result.CallId, out var name) || name != result.Name) throw new JsonException("Tool result does not match its call.");
            _ = LlmConversationHttpTransport.ParseObject(result.ResultJson);
            items.Add(JsonSerializer.SerializeToElement(new { type = "function_call_output", call_id = result.CallId, output = result.ResultJson }));
        }
    }

    private Result<LlmConversationResponse> Parse(JsonElement root, List<JsonElement> items, Dictionary<string, string> names, int continuationBytes)
    {
        var state = LlmConversationHttpTransport.Text(root, "status");
        if (state == "incomplete") return LlmConversationHttpTransport.TerminalResponse(root, "incomplete");
        if (state == "failed") return LlmConversationHttpTransport.TerminalResponse(root, "refusal");
        if (state != "completed" || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return AiAssistantErrors.InvalidOutput;
        var calls = new List<LlmToolCall>();
        var ids = items.Where(item => LlmConversationHttpTransport.Text(item, "type") == "function_call")
            .Select(item => LlmConversationHttpTransport.RequiredText(item, "call_id")).ToHashSet(StringComparer.Ordinal);
        foreach (var item in output.EnumerateArray())
        {
            var type = LlmConversationHttpTransport.RequiredText(item, "type");
            if (type == "function_call")
            {
                var callId = LlmConversationHttpTransport.RequiredText(item, "call_id");
                var name = LlmConversationHttpTransport.RequiredText(item, "name");
                if (!names.TryGetValue(name, out var original)) return AiAssistantErrors.ToolRejected;
                if (!ids.Add(callId) || callId.Length > 128 || calls.Count >= transport.MaximumToolCalls) return AiAssistantErrors.InvalidOutput;
                var status = LlmConversationHttpTransport.Text(item, "status");
                if (status is not (null or "completed")) return LlmConversationHttpTransport.TerminalResponse(root, "incomplete");
                var arguments = LlmConversationHttpTransport.RequiredText(item, "arguments");
                _ = LlmConversationHttpTransport.ParseObject(arguments);
                calls.Add(new(callId, original, arguments));
            }
            else if (type == "message")
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return AiAssistantErrors.InvalidOutput;
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = LlmConversationHttpTransport.Text(block, "type");
                    if (blockType == "refusal") return LlmConversationHttpTransport.TerminalResponse(root, "refusal");
                    if (blockType != "output_text") return AiAssistantErrors.InvalidOutput;
                }
            }
            else if (type != "reasoning") return AiAssistantErrors.InvalidOutput;
            items.Add(item.Clone());
        }
        if (calls.Count == 0) return AiAssistantErrors.InvalidOutput;
        var continuation = transport.Continuation(AiProvider.OpenAI, items, continuationBytes);
        if (continuation.IsFailure) return continuation.Error;
        return new LlmConversationResponse("tools", calls, continuation.Value, LlmConversationHttpTransport.Usage(root));
    }
}
