using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>
/// Dueño de la clave de los ajustes en HybridCache: <see cref="Persistence.Readers.SystemSettingsReader"/> la llena con
/// ella y este tipo la descarta.
/// </summary>
internal sealed class SystemSettingsCache(HybridCache cache) : ISystemSettingsCache
{
    public const string CacheKey = "settings:system";

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
