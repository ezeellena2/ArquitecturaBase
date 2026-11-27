using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

/// <summary>Consumo (PR12): the usage of a business month and the prices of the models.</summary>
public interface IAiUsageReader
{
    /// <summary>
    /// What the retention kept for <paramref name="month"/> plus the turns still alive submitted between the two instants that reached
    /// the provider, one row per provider and model.
    /// </summary>
    Task<IReadOnlyList<AiUsageRow>> ListMonthAsync(DateOnly month, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiModelPriceRow>> ListPricesAsync(CancellationToken cancellationToken);
}
