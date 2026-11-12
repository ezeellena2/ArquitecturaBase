using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

/// <summary>The business information as knowledge for a channel's assistant: the confirmed facts, read with the permission of the caller.</summary>
internal sealed class AiBusinessKnowledgeSource(IAiBusinessInformationReader reader) : IBusinessKnowledgeSource
{
    public async Task<BusinessKnowledge> ReadAsync(string culture, CancellationToken cancellationToken)
    {
        if (culture is not ("es" or "en")) throw new ArgumentException("The knowledge is read in es or en.", nameof(culture));
        return AiBusinessKnowledgePolicy.Build(await reader.FindAsync(cancellationToken), culture);
    }
}
