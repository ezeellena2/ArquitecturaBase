using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Lo que corta el acceso de una cuenta desde la administración: desactivarla (o activarla), borrarla y desvincular su
/// número. Cada método abre un solo límite con <see cref="CommitPolicy.OnSuccess"/>; al desactivar, borrar o quitar un
/// número, corta además todo acceso ya emitido con <see cref="AccountAccessRevoker"/>, después de tomar el lock de
/// enlaces de la cuenta, que dura lo que el límite.
/// </summary>
internal sealed class UserAccessService(
    IUserReader users,
    IUserRepository repository,
    UserGuard guard,
    ILoginLinkRepository loginLinks,
    PhoneNumberLinker phoneLinker,
    AccountAccessRevoker accessRevoker,
    IUnitOfWork unitOfWork,
    ILogger<UserAccessService> logger) : IUserAccessService
{
    public Task<Result> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "SetUserActive", () =>
            // Desactivar autoguarda el estado y revoca todo el acceso ya emitido (UPDATE inmediatos de OpenIddict): o
            // pasa todo o no pasa nada. Activar es una sola escritura, y va igual dentro del límite: todo método que
            // escribe abre uno.
            unitOfWork.ExecuteInTransactionAsync(
                ct => SetActiveCoreAsync(userId, isActive, ct), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "DeleteUser", () =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => DeleteCoreAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> UnlinkUserPhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "UnlinkUserPhone", () =>
            // Una cuenta que ya no tiene número también es un éxito: suelta un contacto que haya quedado y sus enlaces.
            unitOfWork.ExecuteInTransactionAsync(
                ct => UnlinkPhoneCoreAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> SetActiveCoreAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
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
            var allowed = await guard.EnsureCanBeRemovedAsync(userId, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        await repository.SetActiveAsync(userId, isActive, cancellationToken);

        // IsActive no invalida por sí solo el access token, el refresh token, la cookie ni los enlaces pendientes.
        if (!isActive)
        {
            await accessRevoker.RevokeAsync(userId, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Result> DeleteCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Pone en fila la revocación con la emisión y el canje de enlaces de la cuenta.
        await loginLinks.LockAccountAsync(userId, cancellationToken);

        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        var allowed = await guard.EnsureCanBeRemovedAsync(userId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Antes del borrado: después, el filtro global ya no encuentra la cuenta para renovarle el stamp.
        await accessRevoker.RevokeAsync(userId, cancellationToken);
        await repository.DeleteAsync(userId, cancellationToken);
        return Result.Success();
    }

    // El desvincular del administrador, aparte del propio del perfil: mira EnsurePhoneCanBeUnlinkedAsync y, con número,
    // revoca las sesiones.
    private async Task<Result> UnlinkPhoneCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El bot toma contacto y después cuenta; el orden inverso puede producir un deadlock.
        await phoneLinker.LockAsync(userId, newPhone: null, cancellationToken);

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is null)
        {
            await phoneLinker.ReleaseContactAsync(userId, cancellationToken);
            await phoneLinker.VoidPendingLinksAsync(userId, cancellationToken);
            return Result.Success();
        }

        var allowed = await guard.EnsurePhoneCanBeUnlinkedAsync(user, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await phoneLinker.RemovePhoneAsync(userId, cancellationToken);
        await phoneLinker.ReleaseContactAsync(userId, cancellationToken);
        await accessRevoker.RevokeAsync(userId, cancellationToken);
        return Result.Success();
    }
}
