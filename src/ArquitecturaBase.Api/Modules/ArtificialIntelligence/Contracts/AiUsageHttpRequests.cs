using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;

/// <summary>Consumo: the business month to show, as <c>yyyy-MM</c>.</summary>
public sealed record AiUsageQuery(string? Month);

/// <summary>The prices of a model; Revision is the one read, or null to load them the first time.</summary>
public sealed record UpdateAiModelPriceHttpRequest(AiProvider Provider, string Model, decimal? InputUsdPerMillion, decimal? OutputUsdPerMillion,
    decimal? AudioUsdPerMinute, long? Revision);
