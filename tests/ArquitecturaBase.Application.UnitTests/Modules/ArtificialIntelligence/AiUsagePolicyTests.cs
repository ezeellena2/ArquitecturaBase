using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiUsagePolicyTests
{
    private static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    [Fact]
    public void Turns_are_summed_by_business_month_provider_and_model_keeping_unknown_amounts_null()
    {
        AiUsageTurnRow[] turns =
        [
            // The night of the 31st of October in Buenos Aires is already November in UTC: it belongs to October.
            new(new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc), AiProvider.OpenAI, "gpt-6.1-sol", false, 100, 10, null),
            new(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), AiProvider.OpenAI, "gpt-6.1-sol", false, 200, 20, null),
            new(new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), AiProvider.OpenAI, "gpt-6.1-sol", true, null, null, null),
            // A transcription billed by seconds reports no tokens, so the turn stays UsageUnknown: with its seconds it is known.
            new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), AiProvider.OpenAI, "gpt-transcribe", true, null, null, 30.5m),
        ];

        var months = AiUsagePolicy.Summarize(turns, BuenosAires);

        Assert.Equal(3, months.Count);
        var october = Assert.Single(months, row => row.Month == new DateOnly(2026, 10, 1));
        Assert.Equal(new AiUsageRow(AiProvider.OpenAI, "gpt-6.1-sol", 1, 0, 100, 10, null), october.Usage);
        var november = Assert.Single(months, row => row.Month == new DateOnly(2026, 11, 1) && row.Usage.Model == "gpt-6.1-sol");
        Assert.Equal(new AiUsageRow(AiProvider.OpenAI, "gpt-6.1-sol", 2, 1, 200, 20, null), november.Usage);
        var audio = Assert.Single(months, row => row.Usage.Model == "gpt-transcribe");
        Assert.Equal(new AiUsageRow(AiProvider.OpenAI, "gpt-transcribe", 1, 0, null, null, 30.5m), audio.Usage);
    }

    [Fact]
    public void Rows_of_the_same_model_are_combined_and_the_others_stay_apart()
    {
        AiUsageRow[] rows =
        [
            new(AiProvider.OpenAI, "gpt-6.1-sol", 3, 1, 300, null, null),
            new(AiProvider.OpenAI, "gpt-6.1-sol", 2, 0, 200, 50, null),
            new(AiProvider.Anthropic, "gpt-6.1-sol", 1, 0, 10, 1, null),
        ];

        var combined = AiUsagePolicy.Combine(rows);

        Assert.Equal(2, combined.Count);
        Assert.Contains(new AiUsageRow(AiProvider.OpenAI, "gpt-6.1-sol", 5, 1, 500, 50, null), combined);
        Assert.Contains(new AiUsageRow(AiProvider.Anthropic, "gpt-6.1-sol", 1, 0, 10, 1, null), combined);
    }

    [Fact]
    public void The_cost_adds_tokens_by_the_million_and_audio_by_the_minute_rounded_to_cents()
    {
        var usage = new AiUsageRow(AiProvider.OpenAI, "m", 10, 0, 2_315_400, 186_250, 90m);
        var price = new AiModelPriceRow(AiProvider.OpenAI, "m", 1.25m, 10m, 0.006m, 1);

        // 2.3154 × 1.25 + 0.18625 × 10 + 1.5 × 0.006 = 2.89425 + 1.8625 + 0.009 = 4.76575
        Assert.Equal(4.77m, AiUsagePolicy.EstimateCost(usage, price));
    }

    [Fact]
    public void Without_a_price_for_something_it_measured_the_cost_is_unknown()
    {
        var usage = new AiUsageRow(AiProvider.OpenAI, "m", 1, 0, null, null, 60m);

        Assert.Null(AiUsagePolicy.EstimateCost(usage, null));
        Assert.Null(AiUsagePolicy.EstimateCost(usage, new AiModelPriceRow(AiProvider.OpenAI, "m", 1m, 1m, null, 1)));
        Assert.Equal(0.01m, AiUsagePolicy.EstimateCost(usage, new AiModelPriceRow(AiProvider.OpenAI, "m", null, null, 0.006m, 1)));
    }

    [Fact]
    public void A_model_that_reported_nothing_has_no_cost_even_with_prices()
    {
        var usage = new AiUsageRow(AiProvider.OpenAI, "m", 2, 2, null, null, null);

        Assert.Null(AiUsagePolicy.EstimateCost(usage, new AiModelPriceRow(AiProvider.OpenAI, "m", 1m, 1m, 1m, 1)));
    }
}
