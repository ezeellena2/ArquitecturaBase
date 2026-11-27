using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>
/// Consumo de WhatsApp (T7, PR14): the <c>pricing_analytics</c> of the WhatsApp Business Account, read when the screen opens, with a bounded
/// time and no retries, cache or anything saved (ADR 0018).
/// </summary>
public interface IWhatsAppPricingAnalyticsClient
{
    /// <summary>Whether the account can be asked: WhatsApp on, with its account id (<c>WhatsApp:WabaId</c>) and its token.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The daily points between two instants for one number (its digits), by pricing category, and the account's currency.
    /// <c>WhatsApp.Usage.Unavailable</c> if Meta answers an error, does not answer in time or answers something it cannot read.
    /// </summary>
    Task<Result<WhatsAppPricingAnalytics>> ReadAsync(string phoneNumber, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
}
