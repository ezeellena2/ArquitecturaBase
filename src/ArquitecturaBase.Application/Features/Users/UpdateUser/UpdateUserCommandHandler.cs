using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

internal sealed class UpdateUserCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        IReadOnlyCollection<string> roles = [.. command.Roles!.Distinct(StringComparer.Ordinal)];
        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        // Las reglas de la sección 8 del spec, que escribió la Tarea 5: nadie se saca a sí mismo el rol Admin y
        // siempre queda al menos un administrador activo.
        var allowed = await guards.EnsureRolesCanChangeAsync(command.UserId, roles, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await identityService.SetDisplayNameAsync(command.UserId, command.DisplayName, cancellationToken);
        await identityService.SetRolesAsync(command.UserId, roles, cancellationToken);

        return Result.Success();
    }
}
