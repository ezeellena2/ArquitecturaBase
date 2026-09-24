using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Mismo patrón que PermissionService: el valor se cachea en HybridCache y se descarta explícitamente cuando
/// cambia. Sin fila, devuelve InviteOnly, que es el modo cerrado: ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(ApplicationDbContext dbContext, HybridCache cache) : ISystemSettingsReader
{
    public const string CacheKey = "settings:system";

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

    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey,
            dbContext,
            static async (context, token) => await context.SystemSettings
                .AsNoTracking()
                .Select(settings => settings.RegistrationMode)
                .FirstOrDefaultAsync(token),
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
