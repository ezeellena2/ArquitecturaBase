using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Configuración › WhatsApp (RC3-A) with the values a test fixes, without touching the settings row the whole base shares: the readers,
/// the sends, the codes and the retention take them as they take the saved ones. The base's own row is tested in an isolated base.
/// </summary>
internal sealed class FixedWhatsAppSettingsReader(int messageRetentionDays = WhatsAppSettings.DefaultMessageRetentionDays,
    int flowSessionLifetimeHours = WhatsAppSettings.DefaultFlowSessionLifetimeHours, IReadOnlyList<string>? allowedCountries = null) : IWhatsAppSettingsReader
{
    public Task<WhatsAppSettingsResponse> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WhatsAppSettingsResponse(0, flowSessionLifetimeHours, allowedCountries ?? WhatsAppSettings.DefaultAllowedCountries,
            messageRetentionDays));
}
