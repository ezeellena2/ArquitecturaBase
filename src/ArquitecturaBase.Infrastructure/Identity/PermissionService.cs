using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles del usuario (sección 5.6). Los roles del usuario se leen
/// siempre de la base; los permisos de cada rol se cachean y se descartan con <see cref="InvalidateRoleAsync"/>.
/// <para>
/// No se llama adentro de <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>. La fábrica de HybridCache lee
/// con el lector de quien llama, o sea con su contexto y su conexión. Adentro de un límite correría en esa transacción
/// y cachearía por una hora permisos que todavía no se confirmaron. Además, con la protección contra estampidas, otro
/// pedido puede quedar esperando esa misma fábrica: si el dueño del límite se cancela, su rollback encuentra la
/// conexión ocupada y los locks siguen tomados hasta que se descarta el contexto, y el que esperaba termina en un 500
/// cuando ese scope se cierra. Hoy se lee solo afuera de todo límite: la autorización y el perfil. Si alguna vez hace
/// falta adentro, la fábrica pasa a leer en su propio scope, como la de SystemSettingsReader.
/// </para>
/// </summary>
internal sealed class PermissionService(IPermissionReader reader, HybridCache cache) : IPermissionService
{
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromHours(1),
    };

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await reader.GetUserRoleIdsAsync(userId, cancellationToken);

        var permissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var roleId in roleIds)
        {
            permissions.UnionWith(await GetRolePermissionsAsync(roleId, cancellationToken));
        }

        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey(roleId), cancellationToken);

    private async Task<string[]> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey(roleId),
            (reader, roleId),
            static async (state, token) => await state.reader.GetRolePermissionsAsync(state.roleId, token),
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    private static string CacheKey(Guid roleId) =>
        string.Create(CultureInfo.InvariantCulture, $"permissions:role:{roleId:N}");
}
