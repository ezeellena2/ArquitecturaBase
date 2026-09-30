using ArquitecturaBase.Application.Interfaces.Integrations.Caching;

namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>
/// Dueño de la clave de los ajustes en Redis: <see cref="Persistence.Readers.SystemSettingsReader"/> la llena con
/// ella y este tipo la descarta.
/// </summary>
internal sealed class SystemSettingsCache(RedisCache cache) : ISystemSettingsCache
{
    public static readonly RedisCacheKey CacheKey = new("settings:system");
    public static readonly RedisCacheKey PresentationKey = new("settings:presentation");

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKey, cancellationToken);
        await cache.RemoveAsync(PresentationKey, cancellationToken);
    }
}
