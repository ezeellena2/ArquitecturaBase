using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Mismo patrón que PermissionService: el valor se cachea en HybridCache y se descarta explícitamente cuando
/// cambia. Sin fila, devuelve InviteOnly, que es el modo cerrado: ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(IServiceScopeFactory scopeFactory, HybridCache cache) : ISystemSettingsReader
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

    /// <summary>
    /// La fábrica lee en un scope propio, con su contexto y su conexión, y nunca con los de quien llama. Se lee adentro
    /// de los límites del ingreso, de Google y del bot, y con la protección contra estampidas la fábrica puede seguir
    /// sirviendo a otros pedidos después de que el que la arrancó terminó o se canceló. Sobre el contexto de ese pedido
    /// correría adentro de su transacción, vería lo que todavía no confirmó y le ocuparía la conexión que su rollback
    /// necesita para soltar los locks.
    /// </summary>
    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey,
            scopeFactory,
            static async (scopes, token) =>
            {
                await using var scope = scopes.CreateAsyncScope();

                return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SystemSettings
                    .AsNoTracking()
                    .Select(settings => settings.RegistrationMode)
                    .FirstOrDefaultAsync(token);
            },
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
