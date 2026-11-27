using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Consumo de WhatsApp (T7, PR14 to PR16): the month is the business's (<see cref="BusinessMonth"/>, in the zone of Configuración › General),
/// from the current one back to eleven before, because Meta keeps a year. Without the account or the installation's number
/// (<c>WhatsApp:DisplayPhoneNumber</c>) it says so without asking Meta; otherwise it reads <c>pricing_analytics</c> from the start of the month
/// up to its end or now, whichever comes first, and sums it. Nothing is saved or cached: it is an external figure to show (ADR 0018).
/// </summary>
internal sealed class WhatsAppUsageService(IWhatsAppPricingAnalyticsClient analytics, ISystemSettingsReader settings, IOptions<WhatsAppLoginOptions> options,
    IPhoneNumberParser phoneNumbers, TimeProvider timeProvider, ILogger<WhatsAppUsageService> logger) : IWhatsAppUsageService
{
    // Meta keeps the analytics of a year: the current month and the eleven before it.
    private const int MonthsBack = 11;
    private const string MonthField = "month";

    public Task<Result<WhatsAppUsageResponse>> GetAsync(GetWhatsAppUsageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<WhatsAppUsageResponse>>(logger, "GetWhatsAppUsage", async () =>
        {
            if (!BusinessMonth.TryParse(request.Month, out var month)) return FieldErrors.On(WhatsAppUsageErrors.MonthInvalid, MonthField);
            var zone = BusinessDay.Zone((await settings.FindPresentationAsync(cancellationToken)).DefaultTimeZoneId);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var current = BusinessMonth.Of(nowUtc, zone);
            if (month > current || month < current.AddMonths(-MonthsBack)) return FieldErrors.On(WhatsAppUsageErrors.MonthOutOfRange, MonthField);

            var text = BusinessMonth.Format(month);
            var digits = string.Concat((options.Value.DisplayPhoneNumber ?? string.Empty).Where(char.IsAsciiDigit));
            if (!analytics.IsConfigured || digits.Length == 0) return WhatsAppUsageResponse.NotConfigured(text);

            var (startUtc, endUtc) = BusinessMonth.Bounds(month, zone);
            var read = await analytics.ReadAsync(digits, startUtc, endUtc < nowUtc ? endUtc : nowUtc, cancellationToken);
            if (read.IsFailure) return read.Error;
            var phone = phoneNumbers.Parse(null, "+" + digits);
            return WhatsAppUsagePolicy.Summarize(text, phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : "+" + digits, read.Value);
        });
    }
}
