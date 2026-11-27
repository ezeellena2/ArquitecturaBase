using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

/// <summary>The prices of the models (PR13), saved one by one with their revision.</summary>
public interface IAiUsageRepository
{
    /// <summary>Serializes the saving of prices, so two first loads of the same model do not both create it.</summary>
    Task LockPricesAsync(CancellationToken cancellationToken);

    Task<AiModelPrice?> GetPriceAsync(AiProvider provider, string model, CancellationToken cancellationToken);

    void AddPrice(AiModelPrice price);
}
