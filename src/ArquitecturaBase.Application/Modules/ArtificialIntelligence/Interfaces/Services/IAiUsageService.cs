using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

/// <summary>Consumo of the AI (PR12 and PR13): a month's usage by model with its estimated cost, and the prices that estimate it.</summary>
public interface IAiUsageService
{
    Task<Result<AiUsageResponse>> GetMonthAsync(AiUsageRequest request, CancellationToken cancellationToken);

    Task<Result<AiModelPriceResponse>> UpdatePriceAsync(UpdateAiModelPriceRequest request, CancellationToken cancellationToken);
}
