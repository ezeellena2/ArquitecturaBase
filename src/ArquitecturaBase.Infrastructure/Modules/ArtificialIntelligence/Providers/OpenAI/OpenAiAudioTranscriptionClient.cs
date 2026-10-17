using System.Net.Http.Headers;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;

/// <summary>One bounded multipart POST. Quotas, profile, secret policy and durable fences belong to the engine.</summary>
internal sealed class OpenAiAudioTranscriptionClient(
    HttpClient client, AiAssistantLimits limits, TimeProvider timeProvider) : ILlmAudioTranscriptionClient
{
    // Conservative decimal MB ceiling; a qualified profile may lower it.
    internal const int MaximumAudioBytes = 25_000_000;
    internal const int MaximumTextCharacters = 4000;
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/audio/transcriptions");

    public async Task<Result<LlmAudioTranscriptionResponse>> TranscribeAsync(
        LlmAudioTranscriptionRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return AiAssistantErrors.Canceled;
        if (request.Provider != AiProvider.OpenAI || string.IsNullOrWhiteSpace(request.Model)
            || request.Model.Length > 128 || request.Model.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            || string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Any(char.IsControl)
            || request.FileName.Length is < 1 or > 64 || !Path.HasExtension(request.FileName)
            || request.FileName.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            || request.ContentType.Length > 128
            || !MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
            return AiAssistantErrors.Invalid;
        if (request.Content.Length is <= 0 or > MaximumAudioBytes
            || request.MaxTextCharacters <= 0
            || request.MaxTextCharacters > Math.Min(MaximumTextCharacters, limits.QuestionCharacters)
            || request.MaxResponseBytes <= 0 || request.MaxResponseBytes > limits.ProviderResponseBytes
            || request.TimeoutSeconds <= 0 || request.TimeoutSeconds > limits.ProviderTimeoutSeconds)
            return AiAssistantErrors.LimitExceeded;

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        using var multipart = new MultipartFormDataContent();
        var file = new ReadOnlyMemoryContent(request.Content);
        file.Headers.ContentType = contentType;
        multipart.Add(file, "file", request.FileName);
        multipart.Add(new StringContent(request.Model), "model");
        multipart.Add(new StringContent("json"), "response_format");
        message.Content = multipart;
        try
        {
            // The engine must have committed BeginProviderStep before entering this call.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // Do not parse or log provider error bodies. No retry after a possible POST.
            if ((int)response.StatusCode is >= 400 and < 500) return AiAssistantErrors.ProviderRejected;
            if (!response.IsSuccessStatusCode) return (int)response.StatusCode >= 500
                ? AiAssistantErrors.ProviderUncertain : AiAssistantErrors.ProviderRejected;
            var responseType = response.Content.Headers.ContentType?.MediaType;
            if (responseType is null || !(responseType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || responseType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                return AiAssistantErrors.InvalidOutput;
            if (response.Content.Headers.ContentLength > request.MaxResponseBytes) return AiAssistantErrors.LimitExceeded;
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, linked.Token)) > 0)
            {
                if (body.Length + read > request.MaxResponseBytes) return AiAssistantErrors.LimitExceeded;
                body.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, checked((int)body.Length)),
                new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !LlmConversationHttpTransport.HasUniqueProperties(document.RootElement))
                return AiAssistantErrors.InvalidOutput;
            return Parse(document.RootElement, request.MaxTextCharacters);
        }
        catch (OperationCanceledException) { return AiAssistantErrors.ProviderUncertain; }
        catch (HttpRequestException) { return AiAssistantErrors.ProviderUncertain; }
        catch (IOException) { return AiAssistantErrors.ProviderUncertain; }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
    }

    private static Result<LlmAudioTranscriptionResponse> Parse(JsonElement root, int maximumCharacters)
    {
        var text = LlmConversationHttpTransport.Text(root, "text");
        if (string.IsNullOrWhiteSpace(text)) return AiAssistantErrors.InvalidOutput;
        if (text.Length > maximumCharacters) return AiAssistantErrors.LimitExceeded;
        var languages = new List<string>();
        if (root.TryGetProperty("languages", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 8)
                return AiAssistantErrors.InvalidOutput;
            foreach (var item in values.EnumerateArray())
            {
                var code = LlmConversationHttpTransport.Text(item, "code");
                if (code is null || code.Length is < 2 or > 3 || code.Any(character => !char.IsAsciiLetter(character)))
                    return AiAssistantErrors.InvalidOutput;
                var normalizedCode = code.ToLowerInvariant();
                if (languages.Contains(normalizedCode, StringComparer.Ordinal)) return AiAssistantErrors.InvalidOutput;
                languages.Add(normalizedCode);
            }
        }
        LlmConversationUsage? usage = null;
        decimal? audioSeconds = null;
        if (root.TryGetProperty("usage", out var reported) && reported.ValueKind != JsonValueKind.Null)
        {
            if (reported.ValueKind != JsonValueKind.Object) return AiAssistantErrors.InvalidOutput;
            switch (LlmConversationHttpTransport.Text(reported, "type"))
            {
                case "tokens":
                    usage = LlmConversationHttpTransport.Usage(root);
                    break;
                case "duration":
                    if (!reported.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number
                        || !seconds.TryGetDecimal(out var duration) || duration < 0)
                        return AiAssistantErrors.InvalidOutput;
                    audioSeconds = duration;
                    break;
                default:
                    return AiAssistantErrors.InvalidOutput;
            }
        }
        return new LlmAudioTranscriptionResponse(text, languages, usage, audioSeconds);
    }
}
