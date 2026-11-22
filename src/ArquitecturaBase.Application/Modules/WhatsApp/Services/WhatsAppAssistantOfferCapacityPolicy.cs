using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The form offers of a conversation per calendar day in the business's time zone: as many as the saved agent's limit says (TO1-A,
/// «Tope de ofrecimientos de formularios» in Límites), or two until the agent is saved.
/// </summary>
internal sealed class WhatsAppAssistantOfferCapacityPolicy(IWhatsAppAssistantReader reader, IWhatsAppAssistantAgentReader agents, TimeProvider clock)
{
    public async Task<bool> HasCapacityAsync(Guid contactId, AssistantChannelContext channel, CancellationToken ct, Guid? excludingRequestId = null)
    {
        var limit = (await agents.FindMainAsync(ct))?.Limits.DailyOfferLimit ?? WhatsAppAssistantAgentLimits.DefaultDailyOffers;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(channel.TimeZoneId);
        var localDay = WhatsAppBusinessDay.LocalDay(clock.GetUtcNow(), zone);
        var fromUtc = WhatsAppBusinessDay.StartUtc(localDay, zone);
        var toUtc = WhatsAppBusinessDay.StartUtc(localDay.AddDays(1), zone);
        return await reader.CountOffersAsync(contactId, fromUtc, toUtc, ct, excludingRequestId) < limit;
    }
}
