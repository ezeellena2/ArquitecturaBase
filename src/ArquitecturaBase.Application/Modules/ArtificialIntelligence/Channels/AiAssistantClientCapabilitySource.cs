using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

internal sealed class AiAssistantClientCapabilitySource : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures { get; } = ["assistant"];
}
