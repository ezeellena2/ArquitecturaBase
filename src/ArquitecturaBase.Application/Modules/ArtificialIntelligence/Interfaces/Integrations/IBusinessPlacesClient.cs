using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface IBusinessPlacesClient
{
    // Whether the installation has what the search needs (its key); without it the search is a capability turned off (I7).
    bool IsConfigured { get; }
    Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken);
}
