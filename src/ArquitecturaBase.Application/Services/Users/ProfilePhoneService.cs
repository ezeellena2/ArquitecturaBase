using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Desvincula el número propio desde el perfil: conserva otro medio de ingreso, avisa a los participantes e invalida
/// los enlaces pendientes. Con un correo verificado, lo confirma el código que se le mandó a ese correo (cada medio
/// confirma los cambios del otro, 2026-11-16: con una sesión abierta ajena, desvincular el número dejaría afuera a su
/// dueño); el código, de <see cref="LoginCodePurpose.ConfirmChange"/>, lo pide
/// <see cref="ProfileService.RequestEmailConfirmationCodeAsync"/> y lo verifica <see cref="DestinationCodeVerifier"/>.
/// Sin correo verificado, el otro medio es Google, que sigue siendo de la persona, y no hace falta código. Los locks van
/// en el orden único: el correo, los participantes y la cuenta. La persona conserva sus sesiones; solo
/// la administración las revoca al quitar un número.
/// </summary>
internal sealed class ProfilePhoneService(
    ICurrentUser currentUser,
    IUserReader users,
    UserGuard guards,
    PhoneNumberLinker phoneLinker,
    DestinationCodeVerifier verifier,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<ProfilePhoneService> logger) : IProfilePhoneService
{
    /// <summary>El campo del pedido con el código que llegó al correo de la cuenta.</summary>
    public const string CodeField = "code";

    public Task<Result> UnlinkOwnPhoneAsync(UnlinkOwnPhoneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UnlinkOwnPhone", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Un código equivocado cuenta el intento aunque no se desvincule nada. A propósito no revoca sesiones (solo
            // un administrador las corta).
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => UnlinkOwnPhoneCoreAsync(request, ct), CommitPolicy.OnAnyResult, cancellationToken);
        });
    }

    // Desvincular el número propio, ya validado: corre dentro del límite, con OnAnyResult.
    private async Task<Result> UnlinkOwnPhoneCoreAsync(UnlinkOwnPhoneRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        // El orden único de los locks: el destino del correo antes que los participantes y la cuenta (como la edición
        // de la administración). La lectura de antes sólo dice qué correo bloquear; lo que se decide sale de la de
        // después.
        if (await users.FindByIdAsync(userId, cancellationToken) is { } before
            && ProfileService.VerifiedEmailOf(before) is { } lockedEmail)
        {
            await verifier.LockAsync(LoginCodeDestination.ForEmail(lockedEmail), cancellationToken);
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
            // Lo que no se puede hacer se rechaza antes de mirar el código, así no se gasta.
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            if (ProfileService.VerifiedEmailOf(user) is { } email)
            {
                if (string.IsNullOrEmpty(request.Code))
                {
                    return FieldErrors.On(UserErrors.ConfirmationCodeRequired, CodeField);
                }

                var verification = await verifier.VerifyAsync(
                    LoginCodeDestination.ForEmail(email), LoginCodePurpose.ConfirmChange, user.Id, request.Code, cancellationToken);
                if (verification.IsFailure)
                {
                    return verification.Error;
                }
            }

            await phoneLinker.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await phoneLinker.ReleasePhoneAsync(user.Id, cancellationToken);
        await phoneLinker.VoidPendingLinksAsync(user.Id, cancellationToken);
        return Result.Success();
    }
}
