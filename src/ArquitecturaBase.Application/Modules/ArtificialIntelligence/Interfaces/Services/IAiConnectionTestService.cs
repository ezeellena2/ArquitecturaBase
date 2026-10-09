using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
public interface IAiConnectionTestService
{
    Task<Result<AiModelsResponse>> ListModelsAsync(ListAiModelsRequest request, CancellationToken cancellationToken);
    Task<Result<AiConnectionTestResponse>> TestAsync(TestAiConnectionRequest request, CancellationToken cancellationToken);
}
