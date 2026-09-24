using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

internal sealed class UpdateRoleCommandHandler(IIdentityService identityService, IPermissionService permissionService)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await identityService.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var name = command.Name!.Trim();
        IReadOnlyCollection<string> permissions =
            [.. (command.Permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // Admin y User no se renombran (sección 6 del spec de la Fase 4).
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        // Y Admin conserva siempre todos sus permisos: es la garantía de que alguien puede arreglar cualquier cosa.
        // La pantalla manda los que ya tiene, así que reenviarlos iguales no es un cambio.
        if (string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)
            && !permissions.SequenceEqual(role.Permissions, StringComparer.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (await identityService.RoleNameExistsAsync(name, role.Id, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        await identityService.UpdateRoleAsync(role.Id, name, command.Description, permissions, cancellationToken);

        // Sin esto, los permisos viejos seguirían valiendo hasta una hora (PermissionService cachea por rol).
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);

        return Result.Success();
    }
}
