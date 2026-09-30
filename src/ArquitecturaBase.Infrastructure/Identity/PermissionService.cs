using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Effective permissions. Membership is read on every request; Redis entries belong to a confirmed role version.
/// Versions and factories use independent scopes, so uncommitted changes cannot poison a committed cache key.
/// </summary>
internal sealed class PermissionService(IPermissionReader reader, IServiceScopeFactory scopes, RedisCache cache)
    : IPermissionService
{
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

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var version = await FindConfirmedVersionAsync(roleId, cancellationToken);
        if (version is not null)
        {
            await cache.RemoveAsync(CacheKey(roleId, version), cancellationToken);
        }
        // Old versions expire and are never selected by readers of the new committed version.
    }

    private async Task<string[]> ListRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var version = await FindConfirmedVersionAsync(roleId, cancellationToken);
            if (version is null)
            {
                return [];
            }

            try
            {
                var snapshot = await cache.GetOrCreateInOwnScopeAsync<IPermissionReader, (Guid Id, string Version), RolePermissionsRow>(
                    CacheKey(roleId, version), (roleId, version),
                    static async (permissionReader, role, token) =>
                        await permissionReader.FindRolePermissionsAsync(role.Id, role.Version, token)
                            ?? throw new RoleVersionChangedException(),
                    TimeSpan.FromHours(1), cancellationToken);
                return snapshot.Permissions;
            }
            catch (RoleVersionChangedException)
            {
                // The role changed before the factory ran. Resolve the fresh version and retry.
            }
        }

        throw new InvalidOperationException("The role permissions changed repeatedly while reading them.");
    }

    private async Task<string?> FindConfirmedVersionAsync(Guid roleId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionReader>().FindRoleVersionAsync(roleId, cancellationToken);
    }

    private static RedisCacheKey CacheKey(Guid roleId, string version) =>
        new(string.Create(CultureInfo.InvariantCulture, $"permissions:role:{roleId:N}:{version}"));

    private sealed class RoleVersionChangedException : Exception;
}
