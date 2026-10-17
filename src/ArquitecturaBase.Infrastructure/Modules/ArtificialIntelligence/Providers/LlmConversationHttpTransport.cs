using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;

internal sealed class LlmConversationHttpTransport(AiAssistantLimits limits, TimeProvider timeProvider)
{
    private static readonly Meter Meter = new("ArquitecturaBase.AiAssistant", "1.0");
    private static readonly Histogram<int> ContextBytes = Meter.CreateHistogram<int>("ai.assistant.context.bytes", "By");
    private static readonly Histogram<int> ContinuationBytes = Meter.CreateHistogram<int>("ai.assistant.continuation.bytes", "By");

    public int MaximumToolCalls => limits.ToolCalls + 1;

    public async Task<Result<LlmConversationResponse>> SendAsync(HttpClient client, HttpRequestMessage message,
        LlmConversationRequest request, Func<JsonElement, Result<LlmConversationResponse>> parse, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return AiAssistantErrors.Canceled;
        if (request.MaxOutputTokens <= 0 || request.MaxOutputTokens > limits.OutputTokens || request.MaxResponseBytes <= 0
            || request.MaxContinuationBytes <= 0)
            return AiAssistantErrors.LimitExceeded;

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(limits.ProviderTimeoutSeconds), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            if (message.Content is null) return AiAssistantErrors.Invalid;
            var requestBody = await message.Content.ReadAsByteArrayAsync(linked.Token);
            ContextBytes.Record(requestBody.Length);
            if (requestBody.Length > limits.ContextBytes) return AiAssistantErrors.LimitExceeded;
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // These statuses confirm rejection. A server failure cannot establish that generation never started.
            if ((int)response.StatusCode is >= 400 and < 500) return AiAssistantErrors.ProviderRejected;
            if (!response.IsSuccessStatusCode) return (int)response.StatusCode >= 500
                ? AiAssistantErrors.ProviderUncertain : AiAssistantErrors.ProviderRejected;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))) return AiAssistantErrors.InvalidOutput;
            var maximumBytes = Math.Min(request.MaxResponseBytes, limits.ProviderResponseBytes);
            if (response.Content.Headers.ContentLength > maximumBytes) return AiAssistantErrors.LimitExceeded;
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, linked.Token)) > 0)
            {
                if (body.Length + read > maximumBytes) return AiAssistantErrors.LimitExceeded;
                body.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, checked((int)body.Length)),
                new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueProperties(document.RootElement))
                return AiAssistantErrors.InvalidOutput;
            return parse(document.RootElement);
        }
        catch (OperationCanceledException) { return AiAssistantErrors.ProviderUncertain; }
        catch (HttpRequestException) { return AiAssistantErrors.ProviderUncertain; }
        catch (IOException) { return AiAssistantErrors.ProviderUncertain; }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
    }

    public static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueProperties(document.RootElement))
            throw new JsonException("Expected an unambiguous JSON object.");
        return document.RootElement.Clone();
    }

    internal static bool HasUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !HasUniqueProperties(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (!HasUniqueProperties(item)) return false;
        }
        return true;
    }

    public static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static string RequiredText(JsonElement element, string name)
    {
        var value = Text(element, name);
        if (string.IsNullOrWhiteSpace(value)) throw new JsonException("A required protocol string is missing.");
        return value;
    }

    public static LlmConversationUsage? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null) return null;
        if (usage.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid usage.");
        var input = Tokens(usage, "input_tokens");
        var output = Tokens(usage, "output_tokens");
        return input is null && output is null ? null : new(input, output);
    }

    public static LlmConversationResponse TerminalResponse(JsonElement root, string state) => new(state, [], null, Usage(root));

    private static long? Tokens(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var tokens) || tokens < 0)
            throw new JsonException("Invalid token count.");
        return tokens;
    }

    public static string ProviderToolName(string name)
    {
        if (name.Length is < 1 or > 64 || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new JsonException("Invalid tool name.");
        return name.Replace('.', '_');
    }

    public Dictionary<string, string> ToolNames(LlmConversationRequest request)
    {
        if (request.Tools.Count > MaximumToolCalls) throw new JsonException("Too many tool definitions.");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in request.Tools)
            if (!names.TryAdd(ProviderToolName(tool.Name), tool.Name)) throw new JsonException("Ambiguous tool names.");
        return names;
    }

    public List<JsonElement> ReadItems(LlmConversationRequest request, AiProvider provider)
    {
        if (request.Continuation is null)
        {
            if (request.ToolResults.Count != 0) throw new JsonException("Tool results require a continuation.");
            if (request.Messages.Any(message => message.Role is not ("user" or "assistant"))) throw new JsonException("Unsupported conversation role.");
            return request.Messages.Select(message => JsonSerializer.SerializeToElement(new { role = message.Role, content = message.Text })).ToList();
        }
        if (Encoding.UTF8.GetByteCount(request.Continuation) > Math.Min(request.MaxContinuationBytes, limits.ContinuationBytes))
            throw new JsonException("Continuation exceeds its limit.");
        var envelope = ParseObject(request.Continuation);
        if (!envelope.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
            || Text(envelope, "provider") != provider.ToString() || !envelope.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array || items.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
            throw new JsonException("Invalid continuation.");
        return items.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    public Result<string> Continuation(AiProvider provider, IReadOnlyList<JsonElement> items, int maximumBytes)
    {
        var json = JsonSerializer.Serialize(new { version = 1, provider = provider.ToString(), items });
        var bytes = Encoding.UTF8.GetByteCount(json);
        ContinuationBytes.Record(bytes);
        return bytes > Math.Min(maximumBytes, limits.ContinuationBytes)
            ? AiAssistantErrors.LimitExceeded : json;
    }
}
