using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>Private bytes already authorized, bounded and verified by the engine; no HTTP-supplied paths.</summary>
public sealed record LlmAudioTranscriptionRequest(
    AiProvider Provider, string Model, string ApiKey, ReadOnlyMemory<byte> Content,
    string FileName, string ContentType, int MaxTextCharacters, int MaxResponseBytes, int TimeoutSeconds)
{
    public override string ToString() => "LlmAudioTranscriptionRequest { Content and credential = [REDACTED] }";
}

public sealed record LlmAudioTranscriptionResponse(
    string Text, IReadOnlyList<string> Languages, LlmConversationUsage? Usage, decimal? AudioSeconds)
{
    // A provider duration never becomes a fabricated token count.
    public bool TokenUsageUnknown => Usage?.InputTokens is null || Usage?.OutputTokens is null;
    public override string ToString() => "LlmAudioTranscriptionResponse { Content = [REDACTED] }";
}
