using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Configuración › WhatsApp (RC3-A) in memory: before a save it reads the values of before the screen, as the real reader does. A test
/// sets <see cref="Saved"/> to read other values, as if an administrator had saved them; <see cref="Reads"/> counts every use.
/// </summary>
internal sealed class FakeWhatsAppSettings : IWhatsAppSettingsReader, IWhatsAppSettingsRepository
{
    public WhatsAppSettings? Saved { get; set; }
    public List<WhatsAppSettings> Added { get; } = [];
    public int Locks { get; private set; }
    public int Reads { get; private set; }

    /// <summary>Settings saved once with these values, as the screen would leave them.</summary>
    public static FakeWhatsAppSettings With(int flowSessionLifetimeHours = WhatsAppSettings.DefaultFlowSessionLifetimeHours,
        IReadOnlyList<string>? allowedCountries = null, int messageRetentionDays = WhatsAppSettings.DefaultMessageRetentionDays)
    {
        var settings = WhatsAppSettings.Create();
        var saved = settings.Update(flowSessionLifetimeHours, allowedCountries ?? WhatsAppSettings.DefaultAllowedCountries, messageRetentionDays);
        if (saved.IsFailure) throw new ArgumentException("The settings of a test are valid.");
        return new FakeWhatsAppSettings { Saved = settings };
    }

    public Task<WhatsAppSettingsResponse> FindAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(Saved is { } settings
            ? new WhatsAppSettingsResponse(settings.Revision, settings.FlowSessionLifetimeHours, settings.AllowedCountries, settings.MessageRetentionDays)
            : WhatsAppSettingsResponse.Defaults);
    }

    public Task LockAsync(CancellationToken cancellationToken)
    {
        Locks++;
        return Task.CompletedTask;
    }

    public Task<WhatsAppSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

    public void Add(WhatsAppSettings settings)
    {
        Added.Add(settings);
        Saved = settings;
    }
}
