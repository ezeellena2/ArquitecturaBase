using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
public interface IAiConnectionService
{
    Task<Result<AiConnectionResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<AiConnectionChangeResponse>> UpdateAsync(UpdateAiConnectionRequest request, CancellationToken cancellationToken);
    Task<Result<AiConnectionChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken);
}
