using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// `assistant`: Home offers the assistant. `place-search`: the business location offers the place search, only while the installation
// has its key (I7); without it the front does not show the search instead of failing each one.
internal sealed class AiAssistantClientCapabilitySource(IBusinessPlacesClient places) : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures => ["assistant", .. places.IsConfigured ? ["place-search"] : Array.Empty<string>()];
}
