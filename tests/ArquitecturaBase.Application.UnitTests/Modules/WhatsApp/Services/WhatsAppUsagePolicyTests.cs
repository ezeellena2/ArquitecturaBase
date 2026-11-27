using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>Consumo de WhatsApp (T7, PR14): what Meta reports for the month is summed by pricing category, the most expensive first.</summary>
public sealed class WhatsAppUsagePolicyTests
{
    [Fact]
    public void Every_point_is_summed_by_its_category_with_the_most_expensive_first_and_then_by_name()
    {
        WhatsAppPricingPoint[] points =
        [
            new("SERVICE", 1200, 0m),
            new("MARKETING", 70, 4.2m),
            new("UTILITY", 10, 0.13m),
            new("SERVICE", 1280, 0m),
            new("MARKETING", 50, 3.22m),
            new("AUTHENTICATION", 5, 0m),
        ];

        var usage = WhatsAppUsagePolicy.Summarize("2026-11", "+54 9 3406 40-1234", new("USD", points));

        Assert.Equal(("2026-11", true, "+54 9 3406 40-1234", "USD"), (usage.Month, usage.Configured, usage.DisplayPhoneNumber, usage.Currency));
        Assert.Equal(
            [new("MARKETING", 120, 7.42m), new("UTILITY", 10, 0.13m), new("AUTHENTICATION", 5, 0m), new WhatsAppUsageCategoryResponse("SERVICE", 2480, 0m)],
            usage.Categories);
        Assert.Equal((2615L, 7.55m), (usage.TotalDelivered, usage.TotalCost));
    }

    [Fact]
    public void A_category_goes_as_meta_names_it_even_one_the_app_does_not_know()
    {
        WhatsAppPricingPoint[] points = [new("MARKETING_LITE", 3, 0.05m), new("REFERRAL_CONVERSION", 2, 0m), new("SOMETHING_NEW", 1, 0.05m)];

        var usage = WhatsAppUsagePolicy.Summarize("2026-11", "+54 9 3406 40-1234", new("ARS", points));

        Assert.Equal(["MARKETING_LITE", "SOMETHING_NEW", "REFERRAL_CONVERSION"], usage.Categories.Select(category => category.Category));
        Assert.Equal("ARS", usage.Currency);
    }

    [Fact]
    public void A_month_without_messages_has_no_categories_and_zero_totals()
    {
        var usage = WhatsAppUsagePolicy.Summarize("2026-11", "+54 9 3406 40-1234", new("USD", []));

        Assert.Empty(usage.Categories);
        Assert.Equal((0L, 0m), (usage.TotalDelivered, usage.TotalCost));
        Assert.True(usage.Configured);
    }

    [Fact]
    public void Without_whatsapp_configured_the_month_has_nothing_else()
    {
        var usage = WhatsAppUsageResponse.NotConfigured("2026-11");

        Assert.Equal(("2026-11", false, null, null), (usage.Month, usage.Configured, usage.DisplayPhoneNumber, usage.Currency));
        Assert.Empty(usage.Categories);
        Assert.Equal((0L, 0m), (usage.TotalDelivered, usage.TotalCost));
    }
}
