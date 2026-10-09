using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
public interface IBusinessPlacesService
{
    Task<Result<BusinessPlacesResponse>> SearchAsync(SearchBusinessPlacesRequest request, CancellationToken cancellationToken);
}
