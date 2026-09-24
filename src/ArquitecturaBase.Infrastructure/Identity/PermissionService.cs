using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles del usuario (sección 5.6). Los roles del usuario se leen
/// siempre de la base; los permisos de cada rol se cachean y se descartan con <see cref="InvalidateRoleAsync"/>.
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
