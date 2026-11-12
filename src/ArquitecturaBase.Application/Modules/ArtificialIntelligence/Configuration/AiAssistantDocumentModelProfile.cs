using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

/// <summary>
/// Qualifies this exact vision model to read documents, separately from the text and the audio models: the media types it reads
/// (application/pdf, image/jpeg and image/png), the bytes and pages it takes and whether it accepts a reasoning effort.
/// </summary>
public sealed record AiAssistantDocumentModelProfile(AiProvider Provider, string Model, string Id, int Version,
    IReadOnlyList<string> ContentTypes, int MaximumDocumentBytes, int MaximumPages)
{
    /// <summary>The reading asks for a low reasoning effort only when the model takes one; otherwise the request omits it.</summary>
    public bool SupportsReasoningEffort { get; init; }
}
