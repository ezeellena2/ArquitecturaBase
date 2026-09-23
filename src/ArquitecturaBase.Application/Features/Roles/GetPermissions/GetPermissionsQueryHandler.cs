using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

/// <summary>
/// El catálogo sale del propio <see cref="Permissions.All"/>: no hay tabla de permisos. El área es el prefijo del
/// código y los nombres y las descripciones salen de Permissions.resx, en el idioma de la petición.
/// </summary>
internal sealed class GetPermissionsQueryHandler : IQueryHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionGroup>>
{
    public Task<Result<IReadOnlyCollection<PermissionGroup>>> Handle(GetPermissionsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PermissionGroup> groups =
        [
            .. Permissions.All
                .GroupBy(AreaOf, StringComparer.Ordinal)
                .Select(group => new PermissionGroup(
                    group.Key,
                    PermissionTexts.Area(group.Key),
                    [.. group.Select(permission => new PermissionItem(
                        permission,
                        PermissionTexts.Permission(permission),
                        PermissionTexts.Description(permission)))])),
        ];

        return Task.FromResult(Result.Success(groups));
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
