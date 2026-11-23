using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

/// <summary>Configuración › WhatsApp (RC3-A): the three values, saved whole with the revision read.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateWhatsAppSettingsHttpRequest(long ExpectedRevision, int FlowSessionLifetimeHours, IReadOnlyList<string> AllowedCountries,
    int MessageRetentionDays);
