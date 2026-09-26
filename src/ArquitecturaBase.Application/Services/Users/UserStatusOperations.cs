using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Cambia el estado de la cuenta y, al desactivarla o borrarla, corta todos sus medios de acceso ya emitidos. No abre ni
/// confirma transacciones: trabaja dentro del límite de UserService, y el lock de enlaces que toma dura lo que ese límite.
/// </summary>
internal sealed class UserStatusOperations(
    IUserReader users,
    IUserRepository repository,
    UserGuards guards,
    ILoginLinkRepository loginLinks,
    IIdentityService identity)
{
    public async Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        if (!isActive)
        {
            // Pone en fila la revocación con la emisión y el canje de enlaces de la cuenta.
            await loginLinks.LockAccountAsync(userId, cancellationToken);
        }

        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        if (!isActive)
        {
            var allowed = await guards.EnsureCanBeRemovedAsync(userId, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        await repository.SetActiveAsync(userId, isActive, cancellationToken);

        // IsActive no invalida por sí solo el access token, el refresh token ni la cookie.
        if (!isActive)
        {
            await identity.RevokeSessionsAsync(userId, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        await loginLinks.LockAccountAsync(userId, cancellationToken);

        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        var allowed = await guards.EnsureCanBeRemovedAsync(userId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Antes del borrado: después, el filtro global ya no encuentra la cuenta para renovarle el stamp.
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        await repository.DeleteAsync(userId, cancellationToken);
        return Result.Success();
    }
}
