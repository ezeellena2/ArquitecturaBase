using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Roles;

/// <summary>
/// Lecturas y cambios de roles, con sus reglas y la invalidación de permisos cacheados. Es el área de referencia del
/// límite transaccional: cada escritura valida afuera, corre en un solo ExecuteInTransactionAsync y, recién después del
/// commit, invalida el caché.
/// </summary>
internal sealed partial class RoleService(
    IRoleReader roles,
    IRoleRepository repository,
    IPermissionService permissionService,
    ServiceRequestValidator<CreateRoleRequest> createValidator,
    ServiceRequestValidator<UpdateRoleRequest> updateValidator,
    IUnitOfWork unitOfWork,
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

        // El catálogo sale de Permissions.All: áreas y permisos quedan en el orden en que se declaran, con los textos
        // de Permissions.resx en el idioma del pedido.
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

        // Afuera: un pedido inválido no abre transacción.
        if (await createValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "CreateRole", validationError.Code);
            return validationError;
        }

        // Un rol nuevo no lo tiene nadie todavía: no hay caché que invalidar después del commit.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogOutcome("CreateRole", result);
        return result;
    }

    public async Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "UpdateRole");

        // 1. Afuera: validar el pedido. Un pedido inválido no abre transacción ni toma locks.
        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "UpdateRole", validationError.Code);
            return validationError;
        }

        // 2. Un solo límite, con la política escrita: un error de negocio no deja nada.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        // 3. Afuera, después del commit y solo si se confirmó. Invalidar antes dejaría que una lectura concurrente vuelva
        //    a cachear los permisos viejos durante una hora.
        if (result.IsSuccess)
        {
            await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
        }

        LogOutcome("UpdateRole", result);
        return result;
    }

    public async Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "DeleteRole");

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => DeleteCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
        }

        LogOutcome("DeleteRole", result);
        return result;
    }

    // Adentro va todo lo que lee para decidir y todo lo que escribe, sin logs de éxito ni efectos que dependan del commit.
    private async Task<Result<Guid>> CreateCoreAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        if (await roles.ExistsByNameAsync(name, excludedRoleId: null, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        IReadOnlyCollection<string> permissions = [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal)];

        // RoleManager autoguarda el rol y cada claim: dentro de la transacción, o se guarda todo o no se guarda nada.
        return await repository.CreateAsync(name, request.Description, permissions, cancellationToken);
    }

    private async Task<Result> UpdateCoreAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var name = request.Name!.Trim();
        IReadOnlyCollection<string> permissions =
            [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // Admin y User no se renombran; Admin conserva siempre todos sus permisos.
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)
            && !permissions.SequenceEqual(role.Permissions, StringComparer.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (await roles.ExistsByNameAsync(name, role.Id, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        // RoleManager autoguarda el rol y cada claim: dentro de la transacción, o se guarda todo o no se guarda nada.
        await repository.UpdateAsync(role.Id, name, request.Description, permissions, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> DeleteCoreAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystemRole)
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (role.UserCount > 0)
        {
            return RoleErrors.HasUsers(role.UserCount);
        }

        await repository.DeleteAsync(role.Id, cancellationToken);
        return Result.Success();
    }

    private void LogOutcome(string operation, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, operation);
        }
        else
        {
            LogFailed(logger, operation, result.Error.Code);
        }
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string operation, string errorCode);
}
