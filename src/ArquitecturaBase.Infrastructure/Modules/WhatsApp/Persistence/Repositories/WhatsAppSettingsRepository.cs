using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppSettingsRepository(ApplicationDbContext db) : IWhatsAppSettingsRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => db.AcquireAdvisoryLocksAsync([WhatsAppLockKeys.Settings], cancellationToken);

    public Task<WhatsAppSettings?> GetAsync(CancellationToken cancellationToken) =>
        db.Set<WhatsAppSettings>().SingleOrDefaultAsync(settings => settings.Id == WhatsAppSettings.SingletonId, cancellationToken);

    public void Add(WhatsAppSettings settings) => db.Set<WhatsAppSettings>().Add(settings);
}
