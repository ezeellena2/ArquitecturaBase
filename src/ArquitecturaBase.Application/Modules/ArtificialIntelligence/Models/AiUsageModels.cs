using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>Consumo (PR12, 2026-11-27): the business month to show, as <c>yyyy-MM</c>.</summary>
public sealed record AiUsageRequest(string? Month);

/// <summary>What one model got in a month. A null amount is one no turn reported: unknown, not zero.</summary>
public sealed record AiUsageRow(AiProvider Provider, string Model, long Requests, long UnknownUsageRequests, long? InputTokens,
    long? OutputTokens, decimal? AudioSeconds);

/// <summary>A turn that reached the provider, as the retention adds it to its month before deleting it.</summary>
public sealed record AiUsageTurnRow(DateTime SubmittedAtUtc, AiProvider Provider, string Model, bool UsageUnknown, long? InputTokens,
    long? OutputTokens, decimal? AudioSeconds);

/// <summary>What the turns of a month sent to one model, ready to add to its <see cref="AiUsageMonth"/>.</summary>
public sealed record AiUsageMonthRow(DateOnly Month, AiUsageRow Usage);

public sealed record AiModelPriceRow(AiProvider Provider, string Model, decimal? InputUsdPerMillion, decimal? OutputUsdPerMillion,
    decimal? AudioUsdPerMinute, long Revision);

public sealed record AiUsagePriceResponse(decimal? InputUsdPerMillion, decimal? OutputUsdPerMillion, decimal? AudioUsdPerMinute, long Revision);

public sealed record AiUsageModelResponse(AiProvider Provider, string Model, long Requests, long? InputTokens, long? OutputTokens,
    decimal? AudioSeconds, long UnknownUsageRequests, AiUsagePriceResponse? Price, decimal? EstimatedCostUsd);

/// <summary>
/// The month the screen shows: one row per model, the estimated total of the priced ones, how many have no price and how many
/// requests did not report their usage.
/// </summary>
public sealed record AiUsageResponse(string Month, IReadOnlyList<AiUsageModelResponse> Models, decimal TotalEstimatedCostUsd,
    int UnpricedModels, long UnknownUsageRequests);

/// <summary>The prices to save; Revision is the one read, or null to load them the first time.</summary>
public sealed record UpdateAiModelPriceRequest(AiProvider Provider, string Model, decimal? InputUsdPerMillion, decimal? OutputUsdPerMillion,
    decimal? AudioUsdPerMinute, long? Revision);

public sealed record AiModelPriceResponse(AiProvider Provider, string Model, decimal? InputUsdPerMillion, decimal? OutputUsdPerMillion,
    decimal? AudioUsdPerMinute, long Revision)
{
    public static AiModelPriceResponse Of(AiModelPrice price) => new(price.Provider, price.Model, price.InputUsdPerMillion,
        price.OutputUsdPerMillion, price.AudioUsdPerMinute, price.Revision);
}
