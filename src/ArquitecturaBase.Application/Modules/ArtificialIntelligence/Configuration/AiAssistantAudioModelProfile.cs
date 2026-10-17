using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

/// <summary>Formats and bounds qualify this exact STT model separately from the text model.</summary>
public sealed record AiAssistantAudioModelProfile(AiProvider Provider, string Model, string Id, int Version,
    IReadOnlyList<string> ContentTypes, int MaximumAudioBytes);
