using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.DeleteRole;

internal sealed class DeleteRoleCommandHandler(IIdentityService identityService, IPermissionService permissionService)
    : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await identityService.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystemRole)
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        // El error dice cuántos son, para que se reasignen primero.
        if (role.UserCount > 0)
        {
            return RoleErrors.HasUsers(role.UserCount);
        }

        await identityService.DeleteRoleAsync(role.Id, cancellationToken);
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);

        return Result.Success();
    }
}
