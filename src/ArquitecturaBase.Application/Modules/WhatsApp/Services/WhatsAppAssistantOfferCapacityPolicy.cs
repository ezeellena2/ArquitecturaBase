using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantOfferCapacityPolicy(IWhatsAppAssistantReader reader, TimeProvider clock)
{
    public async Task<bool> HasCapacityAsync(Guid contactId, AssistantChannelContext channel, CancellationToken ct, Guid? excludingRequestId = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(channel.TimeZoneId);
        var localDay = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
        var fromUtc = DayStartUtc(localDay, zone);
        var toUtc = DayStartUtc(localDay.AddDays(1), zone);
        return await reader.CountOffersAsync(contactId, fromUtc, toUtc, ct, excludingRequestId) < 2;
    }

    private static DateTime DayStartUtc(DateTime localDay, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(localDay)) localDay = localDay.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(localDay, zone);
    }
}
