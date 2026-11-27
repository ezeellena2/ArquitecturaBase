using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

/// <summary>
/// Consumo (PR12 and PR13): how turns add up by business month and model, and the cost those amounts get with the prices the
/// administrator loaded. A null amount is unknown and stays null when nothing known is added to it; a cost needs a price for
/// everything it measured. A transcription billed by its seconds reports no tokens: with its seconds it is not unknown.
/// </summary>
public static class AiUsagePolicy
{
    public static IReadOnlyList<AiUsageMonthRow> Summarize(IEnumerable<AiUsageTurnRow> turns, TimeZoneInfo zone) =>
        [.. turns.GroupBy(turn => (Month: BusinessMonth.Of(turn.SubmittedAtUtc, zone), turn.Provider, turn.Model))
            .Select(group => new AiUsageMonthRow(group.Key.Month, new AiUsageRow(group.Key.Provider, group.Key.Model, group.LongCount(),
                group.LongCount(turn => turn.UsageUnknown && turn.AudioSeconds is null), SumKnown(group.Select(turn => turn.InputTokens)),
                SumKnown(group.Select(turn => turn.OutputTokens)), SumKnown(group.Select(turn => turn.AudioSeconds)))))];

    public static IReadOnlyList<AiUsageRow> Combine(IEnumerable<AiUsageRow> rows) =>
        [.. rows.GroupBy(row => (row.Provider, row.Model))
            .Select(group => new AiUsageRow(group.Key.Provider, group.Key.Model, group.Sum(row => row.Requests),
                group.Sum(row => row.UnknownUsageRequests), SumKnown(group.Select(row => row.InputTokens)),
                SumKnown(group.Select(row => row.OutputTokens)), SumKnown(group.Select(row => row.AudioSeconds))))];

    public static decimal? EstimateCost(AiUsageRow usage, AiModelPriceRow? price)
    {
        if (price is null || (usage.InputTokens is null && usage.OutputTokens is null && usage.AudioSeconds is null)) return null;
        var total = 0m;
        if (!TryAdd(usage.InputTokens / 1_000_000m, price.InputUsdPerMillion, ref total)) return null;
        if (!TryAdd(usage.OutputTokens / 1_000_000m, price.OutputUsdPerMillion, ref total)) return null;
        if (!TryAdd(usage.AudioSeconds / 60m, price.AudioUsdPerMinute, ref total)) return null;
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static bool TryAdd(decimal? amount, decimal? unitPrice, ref decimal total)
    {
        if (amount is not > 0) return true;
        if (unitPrice is not { } price) return false;
        total += amount.Value * price;
        return true;
    }

    private static long? SumKnown(IEnumerable<long?> values) =>
        values.Aggregate((long?)null, (sum, value) => value is { } known ? checked((sum ?? 0) + known) : sum);

    private static decimal? SumKnown(IEnumerable<decimal?> values) =>
        values.Aggregate((decimal?)null, (sum, value) => value is { } known ? (sum ?? 0) + known : sum);
}
