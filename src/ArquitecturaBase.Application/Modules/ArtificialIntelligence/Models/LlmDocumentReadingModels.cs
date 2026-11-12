using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>
/// Private bytes of a PDF, a JPEG or a PNG already authorized, bounded and verified by the engine, with the sealed instructions; no
/// HTTP-supplied paths.
/// </summary>
public sealed record LlmDocumentReadingRequest(AiProvider Provider, string Model, string ApiKey, string Instructions,
    ReadOnlyMemory<byte> Content, string ContentType, int MaxOutputTokens, int MaxResponseBytes, int TimeoutSeconds)
{
    public bool LowReasoningEffort { get; init; }
    public override string ToString() => "LlmDocumentReadingRequest { Content and credential = [REDACTED] }";
}

/// <summary>
/// What the provider read, or why not (<c>refusal</c> or <c>incomplete</c>, without a reading), with the usage it reported: absent
/// usage is unknown, never zero.
/// </summary>
public sealed record LlmDocumentReadingResponse(string State, StructuredDocumentReading? Reading, LlmConversationUsage? Usage)
{
    public const string Read = "documentReading";
    public override string ToString() => "LlmDocumentReadingResponse { Content = [REDACTED] }";
}
