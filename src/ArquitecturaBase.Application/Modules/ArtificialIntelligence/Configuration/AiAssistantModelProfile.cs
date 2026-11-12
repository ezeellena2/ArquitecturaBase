using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

public sealed record AiAssistantModelProfile(AiProvider Provider, string Model, int ContextBytes, bool SupportsTools, bool SupportsTerminalAnswer)
{
    public string Id { get; init; } = "";
    public int Version { get; init; } = 1;
    /// <summary>Qualified with the cases of ADR 0021 to draft replies: only then is a capability that drafts text offered.</summary>
    public bool QualifiedToDraft { get; init; }
}
