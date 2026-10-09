using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
internal sealed class AnthropicMessagesClient(HttpClient client, LlmHttpTransport transport) : ILlmProviderClient
{
    public AiProvider Provider => AiProvider.Anthropic;
    public async Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken)
    {
        var models = new List<AiModelResponse>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        for (var page = 0; page < 20; page++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, "v1/models?limit=100" + (after is null ? "" : "&after_id=" + Uri.EscapeDataString(after)));
            message.Headers.Add("x-api-key", request.ApiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            var result = await transport.SendAsync(client, message, ParseCatalogPage, cancellationToken);
            if (result.IsFailure) return result.Error;
            models.AddRange(result.Value.Models);
            if (models.Count > LlmModelCatalog.MaxModels) return LlmErrors.ResponseInvalid;
            if (!result.Value.HasMore) return Result.Success(LlmModelCatalog.Sort(models));
            after = result.Value.LastId;
            if (result.Value.Models.Count == 0 || string.IsNullOrEmpty(after) || after.Length > 150 || !cursors.Add(after)) return LlmErrors.ResponseInvalid;
        }
        return LlmErrors.ResponseInvalid;
    }
    private sealed record CatalogPage(IReadOnlyList<AiModelResponse> Models, bool HasMore, string? LastId);
    private static Result<CatalogPage> ParseCatalogPage(JsonElement root)
    {
        var models = LlmModelCatalog.Parse(root);
        if (models.IsFailure) return models.Error;
        if (!root.TryGetProperty("has_more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return LlmErrors.ResponseInvalid;
        return new CatalogPage(models.Value, more.GetBoolean(), LlmHttpTransport.Text(root, "last_id"));
    }
    public async Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        message.Headers.Add("x-api-key", request.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Content = JsonContent.Create(new { model = request.Model, max_tokens = transport.MaxOutputTokens,
            messages = new[] { new { role = "user", content = LlmHttpTransport.TestPrompt } } });
        return await transport.SendAsync(client, message, Parse, cancellationToken);
    }
    private static Result<LlmTestResponse> Parse(JsonElement root)
    {
        if (LlmHttpTransport.Text(root, "type") != "message") return LlmErrors.ResponseInvalid;
        if (LlmHttpTransport.Text(root, "stop_reason") != "end_turn") return LlmErrors.ResponseIncomplete;
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return LlmErrors.ResponseInvalid;
        foreach (var block in content.EnumerateArray())
            if (LlmHttpTransport.Text(block, "type") == "text" && !string.IsNullOrWhiteSpace(LlmHttpTransport.Text(block, "text")))
                return new LlmTestResponse(LlmHttpTransport.Tokens(root, "input_tokens"), LlmHttpTransport.Tokens(root, "output_tokens"));
        return LlmErrors.ResponseIncomplete;
    }
}
