using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Cambia el estado de la cuenta y, al desactivarla, corta todos sus medios de acceso ya emitidos.
/// El lock de enlaces abre una transacción compartida antes del autoguardado de Identity.
/// </summary>
internal sealed class UserStatusOperations(
    IUserReader users,
    IUserRepository repository,
    UserGuards guards,
    ILoginLinkRepository loginLinks,
    IIdentityService identity,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        if (!isActive)
        {
            // Serializa la revocación con la emisión y el canje de enlaces; también hace atómicos los
            // autoguardados de UserManager y la revocación de tokens en el contexto compartido.
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

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        // La transacción cubre los autoguardados de Identity, los tokens y los enlaces.
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

        // El filtro global deja de encontrar al usuario luego del borrado.
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        await repository.DeleteAsync(userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
