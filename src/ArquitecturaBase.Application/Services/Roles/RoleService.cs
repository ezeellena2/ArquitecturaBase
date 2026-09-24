using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Roles;

/// <summary>Lecturas y cambios de roles, con sus reglas y la invalidación de permisos cacheados.</summary>
public sealed partial class RoleService(
    IRoleReader roles,
    IRoleRepository repository,
    IPermissionService permissionService,
    ServiceRequestValidator<CreateRoleRequest> createValidator,
    ServiceRequestValidator<UpdateRoleRequest> updateValidator,
    ILogger<RoleService> logger) : IRoleService
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

    public async Task<Result<Guid>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "CreateRole");

        if (await createValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "CreateRole", validationError.Code);
            return validationError;
        }

        var name = request.Name!.Trim();
        if (await roles.RoleNameExistsAsync(name, excludedRoleId: null, cancellationToken))
        {
            LogFailed(logger, "CreateRole", RoleErrors.AlreadyExistsCode);
            return RoleErrors.AlreadyExists;
        }

        IReadOnlyCollection<string> permissions = [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal)];

        // Un rol nuevo no lo tiene nadie todavía, así que no hay caché que invalidar.
        var roleId = await repository.CreateAsync(name, request.Description, permissions, cancellationToken);
        LogHandled(logger, "CreateRole");

        return roleId;
    }

    public async Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "UpdateRole");

        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "UpdateRole", validationError.Code);
            return validationError;
        }

        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            LogFailed(logger, "UpdateRole", RoleErrors.NotFoundCode);
            return RoleErrors.NotFound;
        }

        var name = request.Name!.Trim();
        IReadOnlyCollection<string> permissions =
            [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // Admin y User no se renombran; Admin conserva siempre todos sus permisos.
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            LogFailed(logger, "UpdateRole", RoleErrors.SystemRoleCannotChangeCode);
            return RoleErrors.SystemRoleCannotChange;
        }

        if (string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)
            && !permissions.SequenceEqual(role.Permissions, StringComparer.Ordinal))
        {
            LogFailed(logger, "UpdateRole", RoleErrors.SystemRoleCannotChangeCode);
            return RoleErrors.SystemRoleCannotChange;
        }

        if (await roles.RoleNameExistsAsync(name, role.Id, cancellationToken))
        {
            LogFailed(logger, "UpdateRole", RoleErrors.AlreadyExistsCode);
            return RoleErrors.AlreadyExists;
        }

        await repository.UpdateAsync(role.Id, name, request.Description, permissions, cancellationToken);

        // El repositorio confirma la transacción antes de descartar el caché de permisos del rol.
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);
        LogHandled(logger, "UpdateRole");

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "DeleteRole");

        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            LogFailed(logger, "DeleteRole", RoleErrors.NotFoundCode);
            return RoleErrors.NotFound;
        }

        if (role.IsSystemRole)
        {
            LogFailed(logger, "DeleteRole", RoleErrors.SystemRoleCannotChangeCode);
            return RoleErrors.SystemRoleCannotChange;
        }

        if (role.UserCount > 0)
        {
            LogFailed(logger, "DeleteRole", RoleErrors.HasUsersCode);
            return RoleErrors.HasUsers(role.UserCount);
        }

        await repository.DeleteAsync(role.Id, cancellationToken);
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);
        LogHandled(logger, "DeleteRole");

        return Result.Success();
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string operation, string errorCode);
}
