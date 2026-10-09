using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
internal interface ILlmProviderClient
{
    AiProvider Provider { get; }
    Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken);
    Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken);
}
