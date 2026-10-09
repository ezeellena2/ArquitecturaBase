using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
internal sealed class OpenAiResponsesClient(HttpClient client, LlmHttpTransport transport) : ILlmProviderClient
{
    public AiProvider Provider => AiProvider.OpenAI;
    public async Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "v1/models");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        return await transport.SendAsync(client, message, LlmModelCatalog.Parse, cancellationToken);
    }
    public Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken) => SendAsync(request, cancellationToken);
    private async Task<Result<LlmTestResponse>> SendAsync(LlmTestRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        message.Content = JsonContent.Create(new { model = request.Model, input = LlmHttpTransport.TestPrompt, max_output_tokens = transport.MaxOutputTokens, store = false });
        return await transport.SendAsync(client, message, Parse, cancellationToken);
    }
    private static Result<LlmTestResponse> Parse(JsonElement root)
    {
        if (LlmHttpTransport.Text(root, "status") != "completed") return LlmErrors.ResponseIncomplete;
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return LlmErrors.ResponseInvalid;
        foreach (var item in output.EnumerateArray())
        {
            if (LlmHttpTransport.Text(item, "type") != "message" || LlmHttpTransport.Text(item, "status") != "completed"
                || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var block in content.EnumerateArray())
                if (LlmHttpTransport.Text(block, "type") == "output_text" && !string.IsNullOrWhiteSpace(LlmHttpTransport.Text(block, "text")))
                    return new LlmTestResponse(LlmHttpTransport.Tokens(root, "input_tokens"), LlmHttpTransport.Tokens(root, "output_tokens"));
        }
        return LlmErrors.ResponseIncomplete;
    }
}
