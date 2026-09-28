using ArquitecturaBase.Application.Common.Logging;
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
internal sealed class RoleService(
    IRoleReader roles,
    IRoleRepository repository,
    IPermissionService permissionService,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<RoleService> logger) : IRoleService
{
    public Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<IReadOnlyCollection<RoleResponse>>>(logger, "GetRoles", async () =>
        {
            var items = await roles.ListRolesAsync(cancellationToken);
            IReadOnlyCollection<RoleResponse> response = [.. items.Select(ToResponse)];

            return Result.Success(response);
        });

    // Una consulta: no valida nada ni abre límite.
    public Task<Result<RoleResponse>> GetRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<RoleResponse>>(logger, "GetRole", async () =>
        {
            if (await roles.FindByIdAsync(roleId, cancellationToken) is not { } role)
            {
                return RoleErrors.NotFound;
            }

            return ToResponse(role);
        });

    public Task<Result<IReadOnlyCollection<PermissionGroupResponse>>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<IReadOnlyCollection<PermissionGroupResponse>>>(logger, "GetPermissions", () =>
        {
            // El catálogo sale de Permissions.All: áreas y permisos quedan en el orden en que se declaran, con los textos
            // de Permissions.resx en el idioma del pedido.
            IReadOnlyCollection<PermissionGroupResponse> groups =
            [
                .. Permissions.All
                    .GroupBy(AreaOf, StringComparer.Ordinal)
                    .Select(group => new PermissionGroupResponse(
                        group.Key,
                        PermissionTexts.Area(group.Key),
                        [.. group.Select(permission => new PermissionItem(
                            permission,
                            PermissionTexts.Permission(permission),
                            PermissionTexts.Description(permission)))])),
            ];

            return Task.FromResult(Result.Success(groups));
        });

    public Task<Result<Guid>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<Guid>>(logger, "CreateRole", async () =>
        {
            // Afuera: un pedido inválido no abre transacción.
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Un rol nuevo no lo tiene nadie todavía: no hay caché que invalidar después del commit.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UpdateRole", async () =>
        {
            // 1. Afuera: validar el pedido. Un pedido inválido no abre transacción ni toma locks.
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // 2. Un solo límite, con la política escrita: un error de negocio no deja nada.
            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

            // 3. Afuera, después del commit y solo si se confirmó. Invalidar antes dejaría que una lectura concurrente
            //    vuelva a cachear los permisos viejos durante una hora.
            if (result.IsSuccess)
            {
                await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
            }

            return result;
        });
    }

    public Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "DeleteRole", async () =>
        {
            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => DeleteCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

            if (result.IsSuccess)
            {
                await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
            }

            return result;
        });
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
        var role = await roles.FindByIdAsync(request.RoleId, cancellationToken);
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
        var role = await roles.FindByIdAsync(request.RoleId, cancellationToken);
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

    // El detalle y cada ítem del catálogo tienen la misma forma.
    private static RoleResponse ToResponse(RoleRow role) =>
        new(role.Id, role.Name, role.Description, role.IsSystemRole, role.UserCount, role.Permissions);

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
