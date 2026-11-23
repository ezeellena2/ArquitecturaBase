using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Configuración › WhatsApp (RC3-A): the saved values, or the ones of before the screen with revision 0. Every use reads it: the time to
/// answer a form when a send is prepared, the countries when a code goes or a number is accepted, and the retention on each clearing and
/// each expiry the chat shows.
/// </summary>
public sealed record WhatsAppSettingsResponse(long Revision, int FlowSessionLifetimeHours, IReadOnlyList<string> AllowedCountries, int MessageRetentionDays)
{
    public static WhatsAppSettingsResponse Defaults { get; } = new(0, WhatsAppSettings.DefaultFlowSessionLifetimeHours,
        WhatsAppSettings.DefaultAllowedCountries, WhatsAppSettings.DefaultMessageRetentionDays);
}

public sealed record UpdateWhatsAppSettingsRequest(long ExpectedRevision, int FlowSessionLifetimeHours, IReadOnlyList<string> AllowedCountries,
    int MessageRetentionDays);
