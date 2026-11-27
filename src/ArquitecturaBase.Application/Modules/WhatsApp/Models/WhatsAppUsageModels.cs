namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>Consumo de WhatsApp (T7): the business month, as <c>yyyy-MM</c>.</summary>
public sealed record GetWhatsAppUsageRequest(string? Month);

/// <summary>
/// Consumo de WhatsApp (T7, PR14): what Meta reports for the installation's number in a business month, summed by pricing category as Meta
/// names it, the most expensive first. Without the account or the number, <see cref="Configured"/> is false and the rest is empty.
/// </summary>
public sealed record WhatsAppUsageResponse(string Month, bool Configured, string? DisplayPhoneNumber, string? Currency,
    IReadOnlyList<WhatsAppUsageCategoryResponse> Categories, long TotalDelivered, decimal TotalCost)
{
    public static WhatsAppUsageResponse NotConfigured(string month) => new(month, false, null, null, [], 0, 0m);
}

/// <summary>The delivered messages of a pricing category and what they cost, in the account's currency.</summary>
public sealed record WhatsAppUsageCategoryResponse(string Category, long Delivered, decimal Cost);

/// <summary>The <c>pricing_analytics</c> of the account: its currency and every data point Meta returned, by pricing category.</summary>
public sealed record WhatsAppPricingAnalytics(string Currency, IReadOnlyList<WhatsAppPricingPoint> Points);

/// <summary>One data point of <c>pricing_analytics</c>: the category as Meta names it, its delivered messages and their cost.</summary>
public sealed record WhatsAppPricingPoint(string Category, long Volume, decimal Cost);
