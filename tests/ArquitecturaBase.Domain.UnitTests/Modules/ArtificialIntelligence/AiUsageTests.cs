using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiUsageTests
{
    [Fact]
    public void A_month_adds_what_it_is_given_and_keeps_unknown_amounts_unknown()
    {
        var month = AiUsageMonth.Create(new DateOnly(2026, 11, 1), AiProvider.OpenAI, "gpt-6.1-sol");

        month.Add(2, 1, null, null, null);
        Assert.Equal(2, month.Requests);
        Assert.Equal(1, month.UnknownUsageRequests);
        Assert.Null(month.InputTokens);
        Assert.Null(month.OutputTokens);
        Assert.Null(month.AudioSeconds);

        month.Add(3, 0, 1_000, 200, 12.5m);
        month.Add(1, 0, 500, null, null);
        Assert.Equal(6, month.Requests);
        Assert.Equal(1_500, month.InputTokens);
        Assert.Equal(200, month.OutputTokens);
        Assert.Equal(12.5m, month.AudioSeconds);
    }

    [Fact]
    public void A_month_is_held_as_its_first_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), AiUsageMonth.Create(new DateOnly(2026, 11, 17), AiProvider.OpenAI, "m").Month);
    }

    [Fact]
    public void A_price_keeps_what_the_administrator_loaded_and_starts_at_revision_one()
    {
        var price = AiModelPrice.Create(AiProvider.OpenAI, " gpt-6.1-sol ", 1.25m, 10m, null);

        Assert.True(price.IsSuccess);
        Assert.Equal("gpt-6.1-sol", price.Value.Model);
        Assert.Equal(1.25m, price.Value.InputUsdPerMillion);
        Assert.Equal(10m, price.Value.OutputUsdPerMillion);
        Assert.Null(price.Value.AudioUsdPerMinute);
        Assert.Equal(1, price.Value.Revision);
    }

    [Theory]
    [InlineData(-0.01, null, null)]
    [InlineData(1000.01, null, null)]
    [InlineData(null, 0.0000001, null)]
    [InlineData(null, null, 10.01)]
    [InlineData(null, null, -1.0)]
    public void A_price_out_of_range_or_with_more_than_six_decimals_is_invalid(double? input, double? output, double? audio)
    {
        var price = AiModelPrice.Create(AiProvider.OpenAI, "m", (decimal?)input, (decimal?)output, (decimal?)audio);

        Assert.Equal(AiModelPriceErrors.Invalid, price.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nmodel")]
    public void A_price_needs_a_model_name(string model)
    {
        Assert.Equal(AiModelPriceErrors.Invalid, AiModelPrice.Create(AiProvider.OpenAI, model, 1m, 1m, null).Error);
    }

    [Fact]
    public void Updating_a_price_needs_its_current_revision_and_only_a_change_moves_it()
    {
        var price = AiModelPrice.Create(AiProvider.OpenAI, "m", 1m, 2m, null).Value;

        Assert.Equal(AiModelPriceErrors.RevisionConflict, price.Update(2, 3m, 4m, null).Error);
        Assert.True(price.Update(1, 1m, 2m, null).IsSuccess);
        Assert.Equal(1, price.Revision);

        Assert.True(price.Update(1, 3m, null, 0.006m).IsSuccess);
        Assert.Equal(2, price.Revision);
        Assert.Equal(3m, price.InputUsdPerMillion);
        Assert.Null(price.OutputUsdPerMillion);
        Assert.Equal(0.006m, price.AudioUsdPerMinute);
        Assert.Equal(AiModelPriceErrors.Invalid, price.Update(2, -1m, null, null).Error);
        Assert.Equal(2, price.Revision);
    }
}
