using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Como PermissionService, cachea en HybridCache, y como él, la fábrica lee en su propio scope
/// (<see cref="HybridCacheExtensions"/>). La clave y su descarte son de <see cref="SystemSettingsCache"/>, que llama el
/// servicio al guardar los ajustes. Sin fila, devuelve InviteOnly, que es el modo cerrado:
/// ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(IServiceScopeFactory scopeFactory, HybridCache cache) : ISystemSettingsReader
{
    /// <summary>
    /// Un minuto, y no una hora como los permisos por rol, a propósito: <b>no subirlo</b>. El servicio descarta
    /// el caché después de guardar los ajustes. El TTL acota cuánto puede durar un valor viejo si se cambia la fila
    /// por fuera del servicio o una lectura concurrente se cruza con la invalidación. Un ajuste que se lee una vez
    /// por ingreso no gana nada con una hora de caché.
    /// </summary>
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// Se lee adentro de los límites del ingreso, de Google y del bot: por eso la fábrica corre en un scope propio, con su
    /// contexto y su conexión. El costo en conexiones lo explica <see cref="HybridCacheExtensions"/>.
    /// </summary>
    public async Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, RegistrationMode>(
            SystemSettingsCache.CacheKey,
            scopeFactory,
            static (db, token) => db.SystemSettings
                .AsNoTracking()
                .Select(settings => settings.RegistrationMode)
                .FirstOrDefaultAsync(token),
            CacheEntryOptions,
            cancellationToken);
}
