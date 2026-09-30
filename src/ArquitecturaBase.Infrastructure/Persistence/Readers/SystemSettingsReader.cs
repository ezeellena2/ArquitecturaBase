using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>Registration mode in Redis for at most one minute. Factories read committed data in their own scope.</summary>
internal sealed class SystemSettingsReader(RedisCache cache) : ISystemSettingsReader
{
    public Task<SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, SystemPresentationResponse>(
            SystemSettingsCache.PresentationKey, 0,
            static async (db, _, token) => await db.SystemSettings.AsNoTracking()
                .Select(settings => new SystemPresentationResponse(settings.DefaultCulture, settings.DefaultTimeZoneId, settings.DefaultPageSize))
                .FirstOrDefaultAsync(token) ?? SystemPresentationResponse.Defaults,
            TimeSpan.FromSeconds(60), cancellationToken);
    public Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, RegistrationMode>(
            SystemSettingsCache.CacheKey, 0,
            static (db, _, token) => db.SystemSettings.AsNoTracking()
                .Select(settings => settings.RegistrationMode).FirstOrDefaultAsync(token),
            TimeSpan.FromSeconds(60), cancellationToken);
}
