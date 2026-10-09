using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
public interface IAiBusinessInformationService
{
    Task<Result<AiBusinessInformationResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<AiBusinessInformationChangeResponse>> UpdateAsync(UpdateAiBusinessInformationRequest request, CancellationToken cancellationToken);
    Task<Result<AiBusinessInformationChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken);
}
