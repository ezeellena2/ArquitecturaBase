using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface ILlmClient
{
    Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken);
    Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken);
}
