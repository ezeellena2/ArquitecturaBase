using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantOfferCapacityPolicy(IWhatsAppAssistantReader reader, TimeProvider clock)
{
    public async Task<bool> HasCapacityAsync(Guid contactId, AssistantChannelContext channel, CancellationToken ct, Guid? excludingRequestId = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(channel.TimeZoneId);
        var localDay = WhatsAppBusinessDay.LocalDay(clock.GetUtcNow(), zone);
        var fromUtc = WhatsAppBusinessDay.StartUtc(localDay, zone);
        var toUtc = WhatsAppBusinessDay.StartUtc(localDay.AddDays(1), zone);
        return await reader.CountOffersAsync(contactId, fromUtc, toUtc, ct, excludingRequestId) < 2;
    }
}
