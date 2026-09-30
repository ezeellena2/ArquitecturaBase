using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Desvincula el número propio desde el perfil: conserva otro medio de ingreso, avisa a los participantes e invalida
/// los enlaces pendientes. La persona conserva sus sesiones; solo la administración las revoca al quitar un número.
/// </summary>
internal sealed class ProfilePhoneService(
    ICurrentUser currentUser,
    IUserReader users,
    UserGuard guards,
    PhoneNumberLinker phoneLinker,
    IUnitOfWork unitOfWork,
    ILogger<ProfilePhoneService> logger) : IProfilePhoneService
{
    public Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "UnlinkOwnPhone", () =>
            // Sin validador: todo va adentro. A propósito no revoca sesiones (solo un administrador las corta).
            unitOfWork.ExecuteInTransactionAsync(UnlinkOwnPhoneCoreAsync, CommitPolicy.OnSuccess, cancellationToken));

    // Desvincular el número propio: corre dentro del límite, con OnSuccess.
    private async Task<Result> UnlinkOwnPhoneCoreAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        await phoneLinker.LockAsync(userId, newPhone: null, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // La persona conserva la sesión actual; solo un administrador revoca sesiones al quitar un número.
        if (user.PhoneNumber is not null)
        {
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            await phoneLinker.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await phoneLinker.ReleasePhoneAsync(user.Id, cancellationToken);
        await phoneLinker.VoidPendingLinksAsync(user.Id, cancellationToken);
        return Result.Success();
    }
}
