using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;

internal sealed class LlmConversationClient(OpenAiConversationClient openAi, AnthropicConversationClient anthropic,
    ILlmAudioTranscriptionClient audio, IPrivateFileStore files, AiAssistantLimits limits, TimeProvider timeProvider) : ILlmConversationClient
{
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken) =>
        request.Audio is not null ? NormalizeAsync(request, cancellationToken) : request.Provider switch
        {
            AiProvider.OpenAI => openAi.SendAsync(request, cancellationToken),
            AiProvider.Anthropic => anthropic.SendAsync(request, cancellationToken),
            _ => Task.FromResult<Result<LlmConversationResponse>>(AiAssistantErrors.Invalid)
        };

    private async Task<Result<LlmConversationResponse>> NormalizeAsync(LlmConversationRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return AiAssistantErrors.Canceled;
        var source = request.Audio!;
        if (request.Provider != AiProvider.OpenAI || request.Messages.Count != 0 || request.Tools.Count != 0 ||
            request.ToolResults.Count != 0 || request.SystemPrompt.Length != 0 || request.Continuation is not null ||
            source.Size is <= 0 or > OpenAiAudioTranscriptionClient.MaximumAudioBytes || request.AudioMaximumBytes <= 0 ||
            request.AudioMaximumBytes > OpenAiAudioTranscriptionClient.MaximumAudioBytes || source.Size > request.AudioMaximumBytes ||
            source.Sha256.Length != 64 || !source.Sha256.All(Uri.IsHexDigit)) return AiAssistantErrors.Invalid;
        if (source.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime) return AiAssistantErrors.Expired;
        if (!files.IsAvailable) return AiAssistantErrors.ContentUnavailable;
        byte[] bytes;
        try
        {
            await using var stream = await files.OpenReadAsync(source.StorageKey, ct);
            if (stream is null) return AiAssistantErrors.ContentUnavailable;
            bytes = new byte[checked((int)source.Size)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), ct);
                if (read == 0) return AiAssistantErrors.ContentUnavailable;
                offset += read;
            }
            var extra = new byte[1];
            if (await stream.ReadAsync(extra, ct) != 0 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                return AiAssistantErrors.ContentUnavailable;
        }
        catch (OperationCanceledException) { return AiAssistantErrors.Canceled; }
        catch (IOException) { return AiAssistantErrors.ContentUnavailable; }
        if (source.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime) return AiAssistantErrors.Expired;
        if (ct.IsCancellationRequested) return AiAssistantErrors.Canceled;
        var result = await audio.TranscribeAsync(new(request.Provider, request.Model, request.ApiKey, bytes,
            source.Name, source.ContentType, limits.QuestionCharacters, request.MaxResponseBytes, limits.ProviderTimeoutSeconds), ct);
        if (result.IsFailure) return result.Error;
        var usage = result.Value.AudioSeconds is { } duration ? (result.Value.Usage ?? new(null, null)) with { AudioSeconds = duration } : result.Value.Usage;
        return new LlmConversationResponse("normalizedAudio", [], null, usage) { NormalizedAudio = new AssistantNormalizedAudio(result.Value.Text, result.Value.Languages) };
    }
}
