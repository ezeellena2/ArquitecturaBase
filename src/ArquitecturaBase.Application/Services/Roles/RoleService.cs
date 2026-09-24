using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Roles;

/// <summary>Lecturas para la administración de roles y su catálogo de permisos.</summary>
public sealed partial class RoleService(IRoleReader roles, ILogger<RoleService> logger) : IRoleService
{
    public async Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, "GetRoles");
        var items = await roles.ListRolesAsync(cancellationToken);
        IReadOnlyCollection<RoleResponse> response =
        [
            .. items.Select(role => new RoleResponse(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystemRole,
                role.UserCount,
                role.Permissions)),
        ];
        LogHandled(logger, "GetRoles");

        return Result.Success(response);
    }

    public Task<Result<IReadOnlyCollection<PermissionGroup>>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, "GetPermissions");

        // El catálogo sale del propio Permissions.All. Se conservan el orden y las traducciones del handler anterior.
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

        LogHandled(logger, "GetPermissions");
        return Task.FromResult(Result.Success(groups));
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);
}
