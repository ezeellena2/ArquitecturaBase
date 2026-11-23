using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

// RC3-A: one row by its key, on every use; before the first save, the values of before the screen.
internal sealed class WhatsAppSettingsReader(ApplicationDbContext db) : IWhatsAppSettingsReader
{
    public async Task<WhatsAppSettingsResponse> FindAsync(CancellationToken cancellationToken) =>
        await db.Set<WhatsAppSettings>().AsNoTracking().Where(settings => settings.Id == WhatsAppSettings.SingletonId)
            .Select(settings => new WhatsAppSettingsResponse(settings.Revision, settings.FlowSessionLifetimeHours, settings.AllowedCountries,
                settings.MessageRetentionDays))
            .SingleOrDefaultAsync(cancellationToken) ?? WhatsAppSettingsResponse.Defaults;
}
