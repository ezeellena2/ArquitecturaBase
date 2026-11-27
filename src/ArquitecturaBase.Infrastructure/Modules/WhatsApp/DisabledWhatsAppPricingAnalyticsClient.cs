using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>With WhatsApp off there is no account to ask: Consumo says WhatsApp is not configured without calling Meta.</summary>
internal sealed class DisabledWhatsAppPricingAnalyticsClient : IWhatsAppPricingAnalyticsClient
{
    public bool IsConfigured => false;

    public Task<Result<WhatsAppPricingAnalytics>> ReadAsync(string phoneNumber, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken) =>
        Task.FromResult<Result<WhatsAppPricingAnalytics>>(WhatsAppUsageErrors.Unavailable);
}
