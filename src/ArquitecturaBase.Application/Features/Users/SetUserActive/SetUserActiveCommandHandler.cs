using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.SetUserActive;

internal sealed class SetUserActiveCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<SetUserActiveCommand>
{
    public async Task<Result> Handle(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        if (!command.IsActive)
        {
            // Las reglas de la sección 8 del spec, que escribió la Tarea 5: nunca la propia cuenta ni el último
            // administrador activo. Activar no saca acceso a nadie, así que no pasa por acá.
            var allowed = await guards.EnsureCanBeRemovedAsync(command.UserId, cancellationToken);

            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        await identityService.SetActiveAsync(command.UserId, command.IsActive, cancellationToken);

        // Marcar la fila no impide nada: el access token vale 15 minutos y la cookie, 30 días (sección 7 del spec).
        if (!command.IsActive)
        {
            await identityService.RevokeSessionsAsync(command.UserId, cancellationToken);
        }

        return Result.Success();
    }
}
