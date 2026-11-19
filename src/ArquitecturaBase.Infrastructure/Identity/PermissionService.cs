using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos, sin caché (N9 y N33, 2026-11-16): una consulta a la base por persona y por scope, que se memoriza en
/// ese scope, así las políticas que preguntan varias veces en un pedido no repiten la consulta. Lee con el contexto del
/// pedido: adentro de un límite usa su conexión y su transacción. Son los del catálogo más lo que incluyen
/// (<see cref="IPermissionCatalog.Effective"/>): el claim de un módulo quitado no habilita nada, y un rol guardado con
/// «Administrar usuarios» y sin «Ver usuarios» igual ve usuarios.
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
            .. catalog.Effective(await reader.ListPermissionsForUserAsync(userId, cancellationToken))
                .Order(StringComparer.Ordinal),
        ];

        _byUser[userId] = permissions;
        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);
}
