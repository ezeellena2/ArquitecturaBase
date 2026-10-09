using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record AiModelsResponse(AiProvider Provider, long Revision, IReadOnlyList<AiModelResponse> Models);
