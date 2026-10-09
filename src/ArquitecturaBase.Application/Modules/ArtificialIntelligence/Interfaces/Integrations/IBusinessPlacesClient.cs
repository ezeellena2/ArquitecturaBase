using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface IBusinessPlacesClient
{
    Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken);
}
