using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos, sin caché (N9 y N33, 2026-11-16): una consulta a la base por persona y por scope, que se memoriza en
/// ese scope, así las políticas que preguntan varias veces en un pedido no repiten la consulta. Lee con el contexto del
/// pedido: adentro de un límite usa su conexión y su transacción. Sólo cuentan los permisos del catálogo: el claim de un
/// módulo quitado no habilita nada.
/// </summary>
internal sealed class PermissionService(IPermissionReader reader, IPermissionCatalog catalog) : IPermissionService
{
    private readonly Dictionary<Guid, IReadOnlyCollection<string>> _byUser = [];

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_byUser.TryGetValue(userId, out var known))
        {
            return known;
        }

        IReadOnlyCollection<string> permissions =
        [
            .. (await reader.ListPermissionsForUserAsync(userId, cancellationToken))
                .Where(catalog.Contains)
                .Order(StringComparer.Ordinal),
        ];

        _byUser[userId] = permissions;
        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);
}
