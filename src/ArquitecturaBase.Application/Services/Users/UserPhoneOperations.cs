using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>Desvinculación administrativa: bloquea contacto y cuenta antes de leer y modificar la cuenta.</summary>
internal sealed class UserPhoneOperations(
    IUserReader users,
    IUserRepository repository,
    UserGuards guards,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange,
    IIdentityService identity,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> UnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El bot toma contacto y después cuenta; el orden inverso puede producir un deadlock.
        await phoneChange.LockAsync(userId, newPhone: null, cancellationToken);

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is null)
        {
            await contactLinker.UnlinkUserAsync(userId, cancellationToken);
            await phoneChange.VoidPendingLinksAsync(userId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var allowed = await guards.EnsurePhoneCanBeUnlinkedAsync(user, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await repository.RemovePhoneAsync(userId, cancellationToken);
        await contactLinker.UnlinkUserAsync(userId, cancellationToken);
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
