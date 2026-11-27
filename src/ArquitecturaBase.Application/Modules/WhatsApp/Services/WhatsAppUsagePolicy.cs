using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Consumo de WhatsApp (T7, PR14): sums the delivered messages and the cost of every point Meta returned by its pricing category, kept as Meta
/// names it (an unknown one too), the most expensive first and then by name.
/// </summary>
internal static class WhatsAppUsagePolicy
{
    public static WhatsAppUsageResponse Summarize(string month, string displayPhoneNumber, WhatsAppPricingAnalytics analytics)
    {
        WhatsAppUsageCategoryResponse[] categories =
        [
            .. analytics.Points
                .GroupBy(point => point.Category, StringComparer.Ordinal)
                .Select(group => new WhatsAppUsageCategoryResponse(group.Key, group.Sum(point => point.Volume), group.Sum(point => point.Cost)))
                .OrderByDescending(category => category.Cost)
                .ThenBy(category => category.Category, StringComparer.Ordinal),
        ];
        return new(month, true, displayPhoneNumber, analytics.Currency, categories, categories.Sum(category => category.Delivered),
            categories.Sum(category => category.Cost));
    }
}
