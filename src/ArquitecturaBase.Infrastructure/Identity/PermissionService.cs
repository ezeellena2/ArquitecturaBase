using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles del usuario (sección 5.6). Los roles del usuario se leen
/// siempre de la base, con el lector de quien llama; los permisos de cada rol se cachean y se descartan con
/// <see cref="InvalidateRoleAsync"/>.
/// <para>
/// La fábrica del caché lee en su propio scope (<see cref="HybridCacheExtensions"/>), así que se puede llamar adentro de
/// un límite: no ve lo que quien llama todavía no confirmó ni le ocupa la conexión.
/// </para>
/// </summary>
internal sealed class PermissionService(IPermissionReader reader, IServiceScopeFactory scopes, HybridCache cache)
    : IPermissionService
{
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromHours(1),
    };

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await reader.ListRoleIdsForUserAsync(userId, cancellationToken);

        var permissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var roleId in roleIds)
        {
            permissions.UnionWith(await ListRolePermissionsAsync(roleId, cancellationToken));
        }

        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey(roleId), cancellationToken);

    private async Task<string[]> ListRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateInOwnScopeAsync<IPermissionReader, Guid, string[]>(
            CacheKey(roleId),
            scopes,
            roleId,
            static (permissionReader, id, token) => permissionReader.ListPermissionsForRoleAsync(id, token),
            CacheEntryOptions,
            cancellationToken);

    private static string CacheKey(Guid roleId) =>
        string.Create(CultureInfo.InvariantCulture, $"permissions:role:{roleId:N}");
}
