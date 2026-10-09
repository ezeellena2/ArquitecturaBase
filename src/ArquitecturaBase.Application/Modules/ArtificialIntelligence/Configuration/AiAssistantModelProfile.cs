using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

public sealed record AiAssistantModelProfile(AiProvider Provider, string Model, int ContextBytes, bool SupportsTools, bool SupportsTerminalAnswer);
