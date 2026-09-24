using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Settings;

/// <summary>
/// Mismo patrón que PermissionService: el valor se cachea en HybridCache y se descarta explícitamente cuando
/// cambia. Sin fila, devuelve InviteOnly, que es el modo cerrado: ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(ApplicationDbContext dbContext, HybridCache cache) : ISystemSettingsReader
{
    public const string CacheKey = "settings:system";

    /// <summary>
    /// Un minuto, y no una hora como los permisos por rol, a propósito: <b>no subirlo</b>. El comando que guarda
    /// los ajustes descarta el caché antes de que UnitOfWork confirme el guardado, así que una lectura
    /// que caiga justo en esa ventana vuelve a cachear el valor viejo; el TTL es el techo de cuánto puede durar
    /// eso. Un ajuste que se lee una vez por ingreso no gana nada con una hora de caché.
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
