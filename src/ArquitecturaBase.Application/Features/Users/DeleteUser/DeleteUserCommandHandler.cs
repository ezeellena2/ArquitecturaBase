using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.DeleteUser;

internal sealed class DeleteUserCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<DeleteUserCommand>
{
    public async Task<Result> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        // Las mismas reglas que desactivar (Tarea 5): nunca la propia cuenta ni el último administrador activo.
        var allowed = await guards.EnsureCanBeRemovedAsync(command.UserId, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Primero revocar y después borrar: una vez borrado, el filtro global lo esconde y ya no se lo encuentra.
        await identityService.RevokeSessionsAsync(command.UserId, cancellationToken);
        await identityService.DeleteAsync(command.UserId, cancellationToken);

        return Result.Success();
    }
}
